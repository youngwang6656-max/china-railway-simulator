using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RailSim.Core.Commands;

internal static class ImmutableCommandTypeValidator
{
    private static readonly ConcurrentDictionary<Type, bool> ValidationCache = new();

    public static void EnsureImmutable(ISimulationCommand command)
    {
        var commandType = command.GetType();
        if (ValidationCache.GetOrAdd(
                commandType,
                static type => IsDeeplyImmutable(type, new HashSet<Type>())))
        {
            return;
        }

        throw new ArgumentException(
            $"Command payload type {commandType.FullName} must be deeply immutable.",
            nameof(command));
    }

    private static bool IsDeeplyImmutable(
        Type type,
        HashSet<Type> visiting)
    {
        if (IsKnownImmutable(type))
        {
            return true;
        }

        var nullableValueType = Nullable.GetUnderlyingType(type);
        if (nullableValueType is not null)
        {
            return IsDeeplyImmutable(nullableValueType, visiting);
        }

        if (type.IsArray ||
            typeof(Delegate).IsAssignableFrom(type) ||
            type.IsPointer ||
            type.IsByRef ||
            type.IsByRefLike ||
            type.ContainsGenericParameters)
        {
            return false;
        }

        if (type.IsValueType)
        {
            if (!type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false))
            {
                return false;
            }
        }
        else if (!type.IsSealed)
        {
            return false;
        }

        if (!visiting.Add(type))
        {
            return true;
        }

        try
        {
            foreach (var field in GetInstanceFields(type))
            {
                if (!field.IsInitOnly ||
                    !IsDeeplyImmutable(field.FieldType, visiting))
                {
                    return false;
                }
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                var setter = property.GetSetMethod(nonPublic: true);
                if ((setter is not null && !IsInitOnly(setter)) ||
                    !IsDeeplyImmutable(property.PropertyType, visiting))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static bool IsKnownImmutable(Type type)
    {
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid) ||
            type == typeof(Uri);
    }

    private static bool IsInitOnly(MethodInfo setter)
    {
        return setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit));
    }

    private static IEnumerable<FieldInfo> GetInstanceFields(Type type)
    {
        for (var current = type;
             current is not null && current != typeof(object);
             current = current.BaseType)
        {
            foreach (var field in current.GetFields(
                         BindingFlags.Instance |
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }
}
