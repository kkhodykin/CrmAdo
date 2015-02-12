﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace CrmAdo.EntityFramework.Utils
{
    [DebuggerStepThrough]
    internal static class SharedTypeExtensions
    {
        public static Type UnwrapNullableType(this Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        public static bool IsNullableType(this Type type)
        {
            var typeInfo = type.GetTypeInfo();

            return !typeInfo.IsValueType
                   || (typeInfo.IsGenericType
                       && typeInfo.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        public static Type MakeNullable(this Type type)
        {
            if (type.IsNullableType())
            {
                return type;
            }

            return typeof(Nullable<>).MakeGenericType(type);
        }

        public static bool IsInteger(this Type type)
        {
            type = type.UnwrapNullableType();

            return type == typeof(int)
                   || type == typeof(long)
                   || type == typeof(short)
                   || type == typeof(byte)
                   || type == typeof(uint)
                   || type == typeof(ulong)
                   || type == typeof(ushort)
                   || type == typeof(sbyte);
        }

        public static PropertyInfo GetAnyProperty(this Type type, string name)
        {
            var props = type.GetRuntimeProperties().Where(p => p.Name == name).ToList();
            if (props.Count() > 1)
            {
                throw new AmbiguousMatchException();
            }

            return props.SingleOrDefault();
        }

        private static bool IsNonIntegerPrimitive(this Type type)
        {
            type = type.UnwrapNullableType();

            return type == typeof(bool)
                   || type == typeof(byte[])
                   || type == typeof(char)
                   || type == typeof(DateTime)
                   || type == typeof(DateTimeOffset)
                   || type == typeof(decimal)
                   || type == typeof(double)
                   || type == typeof(float)
                   || type == typeof(Guid)
                   || type == typeof(string)
                   || type == typeof(TimeSpan)
                   || type.GetTypeInfo().IsEnum;
        }

        public static bool IsPrimitive(this Type type)
        {
            return type.IsInteger()
                   || type.IsNonIntegerPrimitive();
        }

        public static Type UnwrapEnumType(this Type type)
        {
            return type.GetTypeInfo().IsEnum ? Enum.GetUnderlyingType(type) : type;
        }
    }
}