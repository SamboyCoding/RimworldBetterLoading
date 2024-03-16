using System;

namespace BetterLoading
{
    public struct TypeCacheKey : IEquatable<TypeCacheKey>
    {
        public readonly string TypeName;
        public readonly string? NamespaceIfAmbiguous;

        public override int GetHashCode() => NamespaceIfAmbiguous == null ? TypeName.GetHashCode() : (17 * 31 + TypeName.GetHashCode()) * 31 + NamespaceIfAmbiguous.GetHashCode();

        public bool Equals(TypeCacheKey other) => string.Equals(TypeName, other.TypeName) && string.Equals(NamespaceIfAmbiguous, other.NamespaceIfAmbiguous);

        public override bool Equals(object obj) => obj is TypeCacheKey other && Equals(other);

        public TypeCacheKey(string typeName, string? namespaceIfAmbiguous = null)
        {
            TypeName = typeName;
            NamespaceIfAmbiguous = namespaceIfAmbiguous;
        }
    }
}