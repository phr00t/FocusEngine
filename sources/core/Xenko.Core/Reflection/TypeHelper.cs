// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Xenko.Core.Annotations;

namespace Xenko.Core.Reflection
{
    public static class TypeHelper
    {
        public static bool IsCollection([NotNull] this Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var typeInfo = type.GetTypeInfo();
            if (typeInfo.IsArray)
            {
                return false;
            }

            if (typeof(IList).GetTypeInfo().IsAssignableFrom(typeInfo))
            {
                return true;
            }

            foreach (var iType in typeInfo.ImplementedInterfaces)
            {
                var iTypeInfo = iType.GetTypeInfo();
                if (iTypeInfo.IsGenericType && iTypeInfo.GetGenericTypeDefinition() == typeof(ICollection<>))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsDictionary([NotNull] this Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var typeInfo = type.GetTypeInfo();
            if (typeof(IDictionary).GetTypeInfo().IsAssignableFrom(typeInfo))
            {
                return true;
            }

            foreach (var iType in typeInfo.ImplementedInterfaces)
            {
                var iTypeInfo = iType.GetTypeInfo();
                if (iTypeInfo.IsGenericType && iTypeInfo.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
