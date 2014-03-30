using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using CrmAdo.Dynamics;
using CrmAdo.Dynamics.Metadata;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SQLGeneration.Builders;
using SQLGeneration.Generators;

namespace CrmAdo
{
    public class SqlGenerationRequestProvider : ICrmRequestProvider
    {

        public const string ParameterToken = "@";

        /// <summary>
        /// Creates a QueryExpression from the given Select command.
        /// </summary>
        /// <returns></returns>
        public OrganizationRequest GetOrganizationRequest(CrmDbCommand command)
        {
            var commandText = command.CommandText;
            var commandBuilder = new CommandBuilder();
            var options = new CommandBuilderOptions();
            options.PlaceholderPrefix = ParameterToken;
            var sqlCommandBuilder = commandBuilder.GetCommand(commandText, options);

            var selectCommandBuilder = sqlCommandBuilder as SelectBuilder;
            if (selectCommandBuilder != null)
            {
                GuardSelectBuilder(selectCommandBuilder);
                var query = FromCommand(selectCommandBuilder, command.Parameters);
                var request = new RetrieveMultipleRequest();
                request.Query = query;
                return request;
            }

            var insertCommandBuilder = sqlCommandBuilder as InsertBuilder;
            if (insertCommandBuilder != null)
            {
                GuardInsertBuilder(insertCommandBuilder);
                var entity = BuildEntityFromInsert(insertCommandBuilder, command);
                var request = new CreateRequest();
                request.Target = entity;
                return request;
            }

            var updateCommandBuilder = sqlCommandBuilder as UpdateBuilder;
            if (updateCommandBuilder != null)
            {
                throw new NotImplementedException();
            }

            var deleteCommandBuilder = sqlCommandBuilder as DeleteBuilder;
            if (deleteCommandBuilder != null)
            {
                throw new NotImplementedException();
            }

            throw new NotSupportedException("Could not translate the command into the appropriate Organization Service Request Message");

        }

        private Entity BuildEntityFromInsert(InsertBuilder insertCommandBuilder, CrmDbCommand command)
        {
            var source = insertCommandBuilder.Table.Source;
            if (!source.IsTable)
            {
                throw new ArgumentException("Can only insert into a table");
            }

            var table = source as Table;

            var metadataProvider = command.CrmDbConnection.MetadataProvider;
            var entityName = GetTableLogicalEntityName(table);

            var entityBuilder = EntityBuilder.WithNewEntity(metadataProvider, entityName);
            // var entityMetadata = metadataProvider.GetEntityMetadata(entity.LogicalName);

            ValueList valuesList = insertCommandBuilder.Values.IsValueList ? insertCommandBuilder.Values as ValueList : null;
            if (valuesList != null)
            {
                var values = valuesList.Values.ToArray();
                int columnOrdinal = 0;
                foreach (var column in insertCommandBuilder.Columns)
                {
                    var columnValue = values[columnOrdinal];
                    bool isParameter;
                    var sqlValue = GetSqlValue(columnValue, command.Parameters, out isParameter);
                    //if (!isParameter)
                    //{
                    // parameters provided directly have no need to be coerced..
                    entityBuilder.WithAttribute(column.Name).SetValueWithTypeCoersion(sqlValue);
                    //  }
                    //  else
                    // {
                    //    entityBuilder.WithAttribute(column.Name).SetValue(sqlValue);
                    // }

                    columnOrdinal++;
                }
            }

            return entityBuilder.Build();
        }

        private object GetSqlValue(IProjectionItem projectionItem, DbParameterCollection parameters, out bool isParameter)
        {
            isParameter = false;
            var literal = projectionItem as Literal;
            if (literal != null)
            {
                return GitLiteralValue(literal);
            }
            var placeholder = projectionItem as Placeholder;
            if (placeholder != null)
            {
                isParameter = true;
                var paramVal = GetParamaterValue<object>(parameters, placeholder.Value);
                return paramVal;
            }
            throw new NotSupportedException();
        }

        private void GuardInsertBuilder(InsertBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException("builder");
            }
            if (builder.Table == null)
            {
                throw new ArgumentException("The insert statement must specify a table name to insert to (this is the logical name of the entity).");
            }
            // if columns specified, then number of values should equate.
            if (builder.Columns.Any())
            {
                if (builder.Values == null || !builder.Values.IsValueList || ((ValueList)builder.Values).Values.Count() != builder.Columns.Count())
                {
                    throw new ArgumentException("There is a mismatch between the number of columns and the number of values specified in the insert statement.");
                }
            }
            //if (!builder.Values > 1)
            //{
            //    throw new NotSupportedException("The select statement must select from a single entity.");
            //}
            //if (!builder.Columns.Any())
            //{
            //    throw new InvalidOperationException("The select statement must select atleast 1 attribute.");
            //}
        }

        public static QueryExpression FromCommand(ICommand command, DbParameterCollection paramaters)
        {
            var query = new QueryExpression();
            //  query.EntityName
            if (command is SelectBuilder)
            {
                var selCommand = command as SelectBuilder;
                AddFrom(selCommand.From, query);
                AddColumns(selCommand.Projection, query);
                if (selCommand.WhereFilterGroup != null)
                {
                    ProcessFilterGroup(query, selCommand.WhereFilterGroup, null, paramaters);
                }
                else
                {
                    AddWhere(selCommand.Where, query, paramaters);
                }
            }
            return query;
        }

        #region Where and Filters

        private static void AddWhere(IEnumerable<IFilter> whereFilter, QueryExpression query, DbParameterCollection paramaters, FilterExpression filterExpression = null)
        {
            // do where clause..
            if (whereFilter != null && whereFilter.Any())
            {
                //TODO: Should only be one where clause?
                foreach (var where in whereFilter)
                {
                    ProcessFilter(where, query, paramaters, filterExpression);
                }
            }
        }

        private static void ProcessFilterGroup(QueryExpression query, FilterGroup filterGroup, FilterExpression filterExpression, DbParameterCollection paramaters)
        {
            if (filterGroup.HasFilters)
            {
                var filter = new FilterExpression();

                // add each filter in this as a condition to the filter..
                AddWhere(filterGroup.Filters, query, paramaters, filter);

                // if there is a filter expression, chain this filter to that one..
                if (filterExpression != null)
                {
                    filterExpression.AddFilter(filter);
                }
                else
                {
                    // this is top level filter expression, add it directly to query.
                    query.Criteria.AddFilter(filter);
                }

                var conjunction = filterGroup.Conjunction;
                if (conjunction == Conjunction.Or)
                {
                    filter.FilterOperator = LogicalOperator.Or;
                }
                else
                {
                    filter.FilterOperator = LogicalOperator.And;
                }
            }
        }

        private static void ProcessFilter(IFilter filter, QueryExpression query, DbParameterCollection paramaters, FilterExpression filterExpression = null, bool negateOperator = false)
        {

            var filterGroup = filter as FilterGroup;
            if (filterGroup != null)
            {
                ProcessFilterGroup(query, filterGroup, filterExpression, paramaters);
                return;
            }

            var orderFilter = filter as OrderFilter;
            if (orderFilter != null)
            {
                bool isLeft;
                Column attColumn;
                var condition = GetCondition(orderFilter, paramaters, out attColumn, out isLeft);
                if (negateOperator)
                {
                    condition.NegateOperator();
                }
                AddFilterCondition(query, filterExpression, condition, attColumn);
                return;
            }

            var nullFilter = filter as NullFilter;
            if (nullFilter != null)
            {
                Column attColumn;
                var condition = GetCondition(nullFilter, out attColumn);
                if (negateOperator)
                {
                    condition.NegateOperator();
                }
                AddFilterCondition(query, filterExpression, condition, attColumn);
                return;
            }

            var inFilter = filter as InFilter;
            if (inFilter != null)
            {
                Column attColumn;
                var condition = GetCondition(inFilter, paramaters, out attColumn);
                if (negateOperator)
                {
                    condition.NegateOperator();
                }
                AddFilterCondition(query, filterExpression, condition, attColumn);
                return;
            }

            var functionFilter = filter as Function;
            if (functionFilter != null)
            {
                Column attColumn;
                var condition = GetCondition(functionFilter, paramaters, out attColumn);
                if (negateOperator)
                {
                    condition.NegateOperator();
                }
                AddFilterCondition(query, filterExpression, condition, attColumn);
                return;
            }

            var notFilter = filter as NotFilter;
            if (notFilter != null)
            {
                var negatedFilter = notFilter.Filter;
                ProcessFilter(negatedFilter, query, paramaters, filterExpression, true);
                return;
            }

            throw new NotSupportedException();

        }

        private static void AddFilterCondition(QueryExpression query, FilterExpression filterExpression, ConditionExpression condition, Column attColumn)
        {

            bool isAlias;
            LinkEntity link = null;
            var sourceEntityName = GetEntityNameOrAliasForSource(query, attColumn.Source, out isAlias, out link);
            condition.EntityName = sourceEntityName;

            // if filter expression present, add it to that.
            if (filterExpression != null)
            {
                filterExpression.AddCondition(condition);
            }
            else
            {
                if (link == null)
                {
                    query.Criteria.Conditions.Add(condition);
                }
                else
                {
                    link.LinkCriteria.Conditions.Add(condition);
                }
            }
        }

        #endregion

        #region From and Joins
        public static void AddFrom(IEnumerable<IJoinItem> @from, QueryExpression query)
        {
            foreach (var f in from)
            {
                if (f != null)
                {
                    if (f is Join)
                    {
                        AddJoin(f as Join, query);
                    }
                    else if (f is AliasedSource)
                    {
                        // only reached if no Joins used in select query.
                        var aliasedSource = f as AliasedSource;
                        if (aliasedSource.Source.IsTable)
                        {
                            var table = aliasedSource.Source as Table;
                            query.EntityName = GetTableLogicalEntityName(table);
                        }
                        else
                        {
                            throw new NotSupportedException("The From keyword must be used in conjunction with a table / entity name. Subqueries not supported.");
                        }

                    }
                }
            }
        }

        public static void AddJoin(Join @join, QueryExpression query)
        {
            if (join != null)
            {

                var binaryJoin = join as BinaryJoin;
                var linkEntity = new LinkEntity();
                bool isFirstFrom = false;
                if (binaryJoin != null)
                {
                    // Recurse to the left..

                    if (binaryJoin.LeftHand != null)
                    {
                        if (binaryJoin.LeftHand.ToString() == "SQLGeneration.Builders.JoinStart")
                        {
                            // this is the very far left hand the join line!
                            isFirstFrom = true;
                        }

                        if (!isFirstFrom)
                        {
                            // There is another join expression to the left - recurse to process that one first..

                            AddJoin(binaryJoin.LeftHand, query);
                        }
                        else
                        {
                            // the first one is special..

                        }
                    }

                    // Are we an inner or outer join?
                    var innerJoin = binaryJoin as InnerJoin;
                    if (innerJoin != null)
                    {
                        linkEntity.JoinOperator = JoinOperator.Inner;
                    }
                    else
                    {
                        var outer = binaryJoin as LeftOuterJoin;
                        if (outer != null)
                        {
                            linkEntity.JoinOperator = JoinOperator.LeftOuter;
                        }
                        else
                        {
                            throw new NotSupportedException("Join type not supported.");
                        }
                    }

                    if (binaryJoin.RightHand != null)
                    {
                        // This is what we are joining on to..
                        AliasedSource asource = binaryJoin.RightHand;
                        if (!string.IsNullOrEmpty(asource.Alias))
                        {
                            linkEntity.EntityAlias = asource.Alias;
                        }
                    }

                }
                var filteredJoin = join as FilteredJoin;
                if (filteredJoin != null)
                {
                    //   stringBuilder.AppendLine(string.Format("{0} On filters:", indent));

                    Column leftColumn = null;
                    Column rightColumn = null;
                    //string leftTableAlias = leftTable
                    Table leftTable = null;
                    Table rightTable = null;
                    int onCount = 0;
                    string leftEntityName = string.Empty;

                    foreach (var on in filteredJoin.OnFilters)
                    {
                        onCount++;
                        // Support Equals
                        var equalTo = on as EqualToFilter;
                        if (equalTo != null)
                        {
                            leftColumn = equalTo.LeftHand as Column;
                            rightColumn = equalTo.RightHand as Column;
                            GuardOnColumn(leftColumn);
                            GuardOnColumn(rightColumn);

                            leftTable = leftColumn.Source.Source as Table;
                            rightTable = rightColumn.Source.Source as Table;
                            leftEntityName = GetTableLogicalEntityName(leftTable);

                            if (isFirstFrom)
                            {
                                // The left table of the first from statement is the main entity
                                query.EntityName = leftEntityName;
                                // detect all columns..
                                // query.ColumnSet = GetColumnSet(projectionItems);
                            }

                            linkEntity.LinkFromEntityName = leftEntityName;
                            linkEntity.LinkToEntityName = GetTableLogicalEntityName(rightTable);
                            linkEntity.LinkFromAttributeName = GetColumnLogicalAttributeName(leftColumn);
                            linkEntity.LinkToAttributeName = GetColumnLogicalAttributeName(rightColumn);

                        }
                        else
                        {
                            throw new NotSupportedException("When using an ON condition, only Equal To operator is supported. For example: INNER JOIN X ON Y.ID = X.ID");
                        }
                        //  stringBuilder.AppendLine(string.Format("{0}  Filter Type: {1}", indent, on.GetType().FullName));
                    }

                    if (onCount > 1)
                    {
                        throw new NotSupportedException("You can only use one ON condition. For example: INNER JOIN X ON Y.ID = X.ID is supported, but INNER JOIN X ON Y.ID = X.ID AND Y.NAME = X.NAME is not supported.");
                    }

                    // add the right columns to the link entities

                    // need to add the link entity onto the query - or onto the appropriate link entity in the chain..
                    // if the left entity name has a link entity in the query, then 
                    LinkEntity existing = null;
                    if (!query.LinkEntities.Any() || isFirstFrom)
                    {
                        if (query.EntityName != linkEntity.LinkFromEntityName)
                        {
                            throw new InvalidOperationException(
                                "The first JOIN in the query must link from the main entity which in your case is " +
                                linkEntity + " but the first join in your query links from: " +
                                linkEntity.LinkFromEntityName + " which is an unknown entity,");
                        }
                        query.LinkEntities.Add(linkEntity);
                    }
                    else
                    {

                        bool isAliased = !string.IsNullOrEmpty(leftColumn.Source.Alias);
                        var match = string.IsNullOrEmpty(leftColumn.Source.Alias)
                                           ? leftEntityName
                                           : leftColumn.Source.Alias;


                        var leftLink = query.FindLinkEntity(match, isAliased);
                        if (leftLink == null)
                        {
                            throw new InvalidOperationException("Could not perform join, as " + match + " is an unknown entity.");
                        }

                        leftLink.LinkEntities.Add(linkEntity);
                    }

                }
            }
        }
        #endregion

        #region Select and Projections
        private static void AddColumns(IEnumerable<AliasedProjection> projection, QueryExpression query)
        {
            if (projection.Count() == 1)
            {
                var aliasedColumn = projection.First().ProjectionItem;
                if (aliasedColumn is AllColumns)
                {
                    query.IncludeAllColumns();
                    return;
                }
            }
            // var projItemsBySource = projection.Where(p=>p.ProjectionItem is Column && ((Column)p.ProjectionItem).Source. ).Where(p=>(.Source)
            foreach (var projItem in projection)
            {
                AddColumn(projItem, query);
            }
        }

        private static void AddColumn(AliasedProjection projection, QueryExpression query)
        {
            string colAlias = projection.Alias;
            if (!string.IsNullOrEmpty(colAlias))
            {
                throw new NotSupportedException("Column aliases are not supported.");
            }

            var col = projection.ProjectionItem as Column;
            if (col != null)
            {
                bool isAliasedSource = !string.IsNullOrEmpty(col.Source.Alias);
                var sourceName = string.IsNullOrEmpty(col.Source.Alias)
                                     ? GetTableLogicalEntityName(col.Source.Source as Table)
                                     : col.Source.Alias;
                var linkItem = query.FindLinkEntity(sourceName, isAliasedSource);
                if (linkItem == null)
                {
                    query.ColumnSet.AddColumn(GetColumnLogicalAttributeName(col));
                    // wehn we can;t find the source, assume default entity..
                    //   throw new InvalidOperationException("Could not find source named: " + sourceName);
                }
                else
                {
                    linkItem.Columns.AddColumn(GetColumnLogicalAttributeName(col));
                }

            }
            else
            {
                throw new NotSupportedException("Only columns that are attribute names are supported.");
            }
        }

        #endregion

        #region Util

        private static void GuardOnColumn(Column column)
        {
            if (column == null)
            {
                throw new NotSupportedException("The ON operator used in the Join statement must have a column name on it's left and right side.");
            }
            if (column.Source == null || column.Source.Source == null)
            {
                throw new NotSupportedException("No column source found for column: " + column.Name + " do you need to prefix that with the table name?");
            }
            if (!column.Source.Source.IsTable)
            {
                throw new NotSupportedException("The ON operator used in the Join statement has a column name that is not from an entity table. Column Name: " + column.Name);
            }
        }

        private static string GetColumnLogicalAttributeName(Column column)
        {
            return column.Name.ToLower();
        }

        // ReSharper disable UnusedParameter.Local
        // This method is solely for pre condition checking.
        private static void GuardSelectBuilder(SelectBuilder builder)
        // ReSharper restore UnusedParameter.Local
        {
            if (builder == null)
            {
                throw new InvalidOperationException("Command Text must be a Select statement.");
            }
            if (!builder.From.Any())
            {
                throw new InvalidOperationException("The select statement must include a From clause.");
            }
            if (builder.From.Count() > 1)
            {
                throw new NotSupportedException("The select statement must select from a single entity.");
            }
            if (!builder.Projection.Any())
            {
                throw new InvalidOperationException("The select statement must select atleast 1 attribute.");
            }
        }

        private static object GitLiteralValue(Literal lit)
        {
            // Support string literals.
            StringLiteral stringLit = null;
            NumericLiteral numberLiteral = null;

            stringLit = lit as StringLiteral;
            if (stringLit != null)
            {
                // cast to GUID?
                Guid val;
                if (Guid.TryParse(stringLit.Value, out val))
                {
                    return val;
                }
                return stringLit.Value;
            }

            numberLiteral = lit as NumericLiteral;
            if (numberLiteral != null)
            {
                // cast down from double to int if possible..
                checked
                {
                    try
                    {
                        if ((numberLiteral.Value % 1) == 0)
                        {
                            int intValue = (int)numberLiteral.Value;
                            if (intValue == numberLiteral.Value)
                            {
                                return intValue;
                            }
                        }

                        // can we return a decimal instead?
                        var decVal = Convert.ToDecimal(numberLiteral.Value);
                        return decVal;
                    }
                    catch (OverflowException)
                    {
                        //   can't down cast to int so remain as double.
                        return numberLiteral.Value;
                    }
                }

            }

            var nullLiteral = lit as NullLiteral;
            if (nullLiteral != null)
            {
                return null;
            }

            throw new NotSupportedException("Unknown Literal");

        }

        private static ConditionExpression GetCondition(InFilter filter, DbParameterCollection paramaters, out Column attColumn)
        {
            var condition = new ConditionExpression();

            var left = filter.LeftHand;
            attColumn = left as Column;

            // defaullt attribute name for the filter condition.
            if (attColumn != null)
            {
                condition.AttributeName = attColumn.Name.ToLower();
            }
            else
            {
                throw new NotSupportedException("IN operator only works agains a column value.");
            }

            var conditionOperator = ConditionOperator.In;
            if (filter.Not)
            {
                conditionOperator = ConditionOperator.NotIn;
            }
            var values = filter.Values;

            if (values.IsValueList)
            {
                var valuesList = values as ValueList;

                if (valuesList != null)
                {
                    var inValues = new object[valuesList.Values.Count()];
                    int index = 0;
                    foreach (var item in valuesList.Values)
                    {
                        var literal = item as Literal;
                        if (literal == null)
                        {
                            throw new ArgumentException("The values list must contain literals.");
                        }
                        inValues[index] = GitLiteralValue(literal);
                        index++;
                    }
                    SetConditionExpressionValue(condition, conditionOperator, inValues);
                    return condition;
                }
                throw new ArgumentException("The values list for the IN expression is null");
            }
            throw new NotSupportedException();
        }

        private static string GetLikeString(string likeString, bool not, out ConditionOperator conditionOperator)
        {
            // detect paramater
            if (string.IsNullOrEmpty(likeString))
            {
                throw new ArgumentException("likestring cannot be null or empty");
            }

            bool startsWith = likeString.EndsWith("%");
            bool endsWith = likeString.StartsWith("%");

            if (startsWith)
            {
                if (endsWith)
                {
                    // contains
                    if (not)
                    {
                        // Does Not contain operator not recognised by Xrm sdk??? 
                        // conditionoperator = ConditionOperator.DoesNotContain;
                        conditionOperator = ConditionOperator.NotLike;
                    }
                    else
                    {
                        //  Contains operator causes Xrm organisation servuce to throw "Generic SQL error"??? 
                        conditionOperator = ConditionOperator.Like;
                    }
                    // val = filter.RightHand.Value.Trim('%');
                }
                else
                {
                    // starts with
                    likeString = likeString.TrimEnd('%');
                    if (not)
                    {
                        conditionOperator = ConditionOperator.DoesNotBeginWith;
                    }
                    else
                    {
                        conditionOperator = ConditionOperator.BeginsWith;
                    }
                }
            }
            else if (endsWith)
            {
                // ends with;
                // contains
                likeString = likeString.TrimStart('%');
                if (not)
                {
                    conditionOperator = ConditionOperator.DoesNotEndWith;
                }
                else
                {
                    conditionOperator = ConditionOperator.EndsWith;
                }
            }
            else
            {
                if (not)
                {
                    conditionOperator = ConditionOperator.NotLike;
                }
                else
                {
                    conditionOperator = ConditionOperator.Like;
                }
            }

            return likeString;
        }

        private static ConditionExpression GetCondition(NullFilter filter, out Column attColumn)
        {
            var condition = new ConditionExpression();

            var left = filter.LeftHand;
            attColumn = left as Column;

            // default attribute name for the filter condition.
            if (attColumn != null)
            {
                condition.AttributeName = attColumn.Name.ToLower();
            }
            else
            {
                throw new NotSupportedException("Null operator only works agains a column value.");
            }

            condition.Operator = ConditionOperator.Null;
            if (filter.Not)
            {
                condition.Operator = ConditionOperator.NotNull;
            }
            return condition;
        }

        private static ConditionExpression GetCondition(OrderFilter filter, DbParameterCollection paramaters, out Column attColumn, out bool isLeft)
        {
            var condition = new ConditionExpression();
            attColumn = GetAttributeColumn(filter, out isLeft);
            if (attColumn != null)
            {
                condition.AttributeName = attColumn.Name.ToLower();
            }

            ConditionOperator con;
            var equalTo = filter as EqualToFilter;
            if (equalTo != null)
            {
                con = ConditionOperator.Equal;
                SetColumnCondition(condition, con, filter, isLeft, paramaters);
                return condition;
            }

            // Support Not Equals
            var notEqualTo = filter as NotEqualToFilter;
            if (notEqualTo != null)
            {
                con = ConditionOperator.NotEqual;
                SetColumnCondition(condition, con, filter, isLeft, paramaters);
                return condition;
            }

            // Support Greater Than
            var greaterThan = filter as GreaterThanFilter;
            if (greaterThan != null)
            {
                con = ConditionOperator.GreaterThan;
                SetColumnCondition(condition, con, filter, isLeft, paramaters);
                return condition;
            }

            // Support Greater Than Equal
            var greaterEqual = filter as GreaterThanEqualToFilter;
            if (greaterEqual != null)
            {
                con = ConditionOperator.GreaterEqual;
                SetColumnCondition(condition, con, filter, isLeft, paramaters);
                return condition;
            }

            // Support Less Than
            var lessThan = filter as LessThanFilter;
            if (lessThan != null)
            {
                con = ConditionOperator.LessThan;
                SetColumnCondition(condition, con, filter, isLeft, paramaters);
                return condition;
            }

            // Support Less Than Equal
            var lessThanEqual = filter as LessThanEqualToFilter;
            if (lessThanEqual != null)
            {
                con = ConditionOperator.LessEqual;
                SetColumnCondition(condition, con, filter, isLeft, paramaters);
                return condition;
            }

            // Support Like
            var likeFilter = filter as LikeFilter;
            if (likeFilter != null)
            {
                var conditionValue = GetConditionValue<string>(likeFilter, isLeft, paramaters);
                ConditionOperator likeOperator;
                var newLike = GetLikeString(conditionValue, likeFilter.Not, out likeOperator);
                SetConditionExpressionValue(condition, likeOperator, newLike);
                return condition;
            }

            throw new NotSupportedException();

        }

        private static ConditionExpression GetCondition(Function functionFilter, DbParameterCollection paramaters, out Column attColumn)
        {
            switch (functionFilter.Name.ToLower())
            {
                case "contains":

                    var condition = new ConditionExpression();
                    var argCount = functionFilter.Arguments.Count();
                    if (argCount != 2)
                    {
                        throw new NotSupportedException("contains function should have exactly 2 arguments.");
                    }

                    //the first argument should be the column name.
                    var args = functionFilter.Arguments.ToArray();

                    attColumn = args[0] as Column;
                    // default attribute name for the filter condition.
                    if (attColumn != null)
                    {
                        condition.AttributeName = attColumn.Name.ToLower();
                    }
                    else
                    {
                        throw new NotSupportedException("the contains function should have a column name as its first argument.");
                    }
                    condition.Operator = ConditionOperator.Contains;

                    var searchArg = args[1];
                    var stringLiteral = searchArg as StringLiteral;
                    if (stringLiteral != null)
                    {
                        condition.Values.Add(stringLiteral.Value);
                        return condition;
                    }

                    // perhaps its a placeholder?
                    var placeholder = searchArg as Placeholder;
                    if (placeholder != null)
                    {
                        var paramVal = GetParamaterValue<object>(paramaters, placeholder.Value);
                        condition.Values.Add(paramVal);
                        return condition;
                    }


                    throw new NotSupportedException("The second argument of the contains function should be either a string literal value, or a parameter");
                //if (functionFilter.Name)
                //{
                //    condition.Operator = ConditionOperator.NotNull;
                //}


                default:

                    throw new NotSupportedException("Unsupported function: '" + functionFilter.Name + "'");
            }
        }

        private static Column GetAttributeColumn(OrderFilter filter, out bool isColumnLeftSide)
        {
            var left = filter.LeftHand;
            var right = filter.RightHand;
            var leftcolumn = left as Column;
            var rightcolumn = right as Column;
            Column attributeColumn = null;
            isColumnLeftSide = false;
            if (leftcolumn != null)
            {

                attributeColumn = leftcolumn;
                isColumnLeftSide = true;

                //// issue with sql generation library, where it doesn't parse @param from sql string as a placeholder, instead it parses it as a column.
                //if (IsParameter(leftcolumn))
                //{

                //}
                //else
                //{
                //    attributeColumn = leftcolumn;
                //    isColumnLeftSide = true;
                //}

            }
            else if (rightcolumn != null)
            {
                attributeColumn = rightcolumn;
                // issue with sql generation library, where it doesn't parse @param from sql string as a placeholder, instead it parses it as a column.
                //if (IsParameter(rightcolumn))
                //{
                //    // filter.LeftHand = new Placeholder(leftcolumn.Name);
                //}
                //else
                //{
                //    attributeColumn = rightcolumn;
                //}
            }

            if (attributeColumn == null)
            {
                throw new InvalidOperationException("The query contains a WHERE clause, however one side of the where condition must refer to an attribute name.");
            }
            return attributeColumn;
        }

        private static string GetEntityNameOrAliasForSource(QueryExpression query, AliasedSource source, out bool isAlias, out LinkEntity linkEntity)
        {
            if (source != null)
            {
                isAlias = !string.IsNullOrEmpty(source.Alias);
                var sourceName = string.IsNullOrEmpty(source.Alias) ? GetTableLogicalEntityName(source.Source as Table) : source.Alias;
                linkEntity = query.FindLinkEntity(sourceName, isAlias);
                if (linkEntity == null)
                {
                    // If this is for the main entity - it doesn;t support alias name..
                    isAlias = false;
                    return query.EntityName;
                }
                return sourceName;
            }
            throw new NotSupportedException("A condition in the WHERE clause contains refers to an unknown table / entity.");
        }

        private static string GetTableLogicalEntityName(Table table)
        {
            return table.Name.ToLower();
        }

        private static T GetConditionValue<T>(OrderFilter filter, bool isLeft, DbParameterCollection paramaters)
        {
            // check for literals..
            Literal lit = null;
            if (isLeft)
            {
                lit = filter.RightHand as Literal;
            }
            else
            {
                lit = filter.LeftHand as Literal;
            }

            if (lit != null)
            {
                object litVal = GitLiteralValue(lit);
                return (T)litVal;
            }

            // check for placeholders..
            Placeholder placeholder = null;
            if (isLeft)
            {
                placeholder = filter.RightHand as Placeholder;
            }
            else
            {
                placeholder = filter.LeftHand as Placeholder;
            }

            if (placeholder != null)
            {
                return GetParamaterValue<T>(paramaters, placeholder.Value);
            }

            throw new NotSupportedException("Could not get value of type: " + typeof(T).FullName);


        }

        private static void SetColumnCondition(ConditionExpression condition, ConditionOperator conditionOperator, OrderFilter filter, bool isLeft, DbParameterCollection paramaters)
        {
            // check for literals..
            Literal lit = null;
            if (isLeft)
            {
                lit = filter.RightHand as Literal;
            }
            else
            {
                lit = filter.LeftHand as Literal;
            }

            if (lit != null)
            {
                object litVal = GitLiteralValue(lit);
                SetConditionExpressionValue(condition, conditionOperator, litVal);
                return;
            }

            // check for paramaters - these should be placeholders but sqlgeneration doesn't parse them correctly so they are columns.
            Placeholder placeholder = null;
            if (isLeft)
            {
                placeholder = filter.RightHand as Placeholder;
            }
            else
            {
                placeholder = filter.LeftHand as Placeholder;
            }

            if (placeholder != null)
            {
                var param = GetParamaterValue<object>(paramaters, placeholder.Value);
                SetConditionExpressionValue(condition, conditionOperator, param);
                return;
            }

            throw new NotSupportedException();
        }

        private static void SetConditionExpressionValue(ConditionExpression condition, ConditionOperator conditionOperator, params object[] values)
        {
            condition.Operator = conditionOperator;
            if (values != null)
            {
                // is the literal a 
                foreach (var value in values)
                {
                    if (value is Array)
                    {
                        foreach (var o in value as Array)
                        {
                            condition.Values.Add(o);
                        }
                    }
                    else
                    {
                        condition.Values.Add(value);
                    }
                }
            }
        }

        private static T GetParamaterValue<T>(DbParameterCollection parameters, string paramName)
        {
            if (!parameters.Contains(paramName))
            {
                throw new InvalidOperationException("Missing parameter value for parameter named: " + paramName);
            }
            var param = parameters[paramName];
            return (T)param.Value;
        }

        #endregion

    }
}