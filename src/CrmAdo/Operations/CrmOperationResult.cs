﻿using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrmAdo.Operations
{
    public class CrmOperationResult : ICrmOperationResult
    {
        public bool UseResultCountAsReturnValue { get; set; }

        public CrmOperationResult(OrganizationResponse response, ResultSet resultSet, bool useResultCountAsReturnValue)
        {
            this.Response = response;
            this.ResultSet = resultSet;         
            UseResultCountAsReturnValue = useResultCountAsReturnValue;
        }

        public OrganizationResponse Response { get; set; }
        public ResultSet ResultSet { get; set; }

        public int ReturnValue
        {
            get
            {
                if (UseResultCountAsReturnValue)
                {
                    if (ResultSet != null)
                    {
                        return ResultSet.ResultCount();
                    }
                }
                return -1;
            }
        }
        
        public virtual bool HasMoreResults
        {
            get { return false; }
        }

        public virtual void NextOperationResult()
        {
            throw new InvalidOperationException();
        }       

        public DbDataReader GetReader(DbConnection connection = null)
        {
            return new CrmDbDataReader(this, connection);
        }      
      
     
    }
}
