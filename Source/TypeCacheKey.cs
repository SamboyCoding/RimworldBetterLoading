using System;

namespace BetterLoading
{
    public struct TypeCacheKey : IEquatable<TypeCacheKey>
    {
        public string typeName;
        public string namespaceIfAmbiguous;

        public override int GetHashCode() => namespaceIfAmbiguous == null ? typeName.GetHashCode() : (17 * 31 + typeName.GetHashCode()) * 31 + namespaceIfAmbiguous.GetHashCode();

        public bool Equals(TypeCacheKey other) => string.Equals(typeName, other.typeName) && string.Equals(namespaceIfAmbiguous, other.namespaceIfAmbiguous);

        public override bool Equals(object obj) => obj is TypeCacheKey other && Equals(other);

        public TypeCacheKey(string typeName, string namespaceIfAmbigous = null)
        {
            this.typeName = typeName;
            namespaceIfAmbiguous = namespaceIfAmbigous;
        }
    }
}