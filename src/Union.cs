using System;

namespace JumpSlug;

class InvalidUnionVariantException : Exception {
    public InvalidUnionVariantException() {}

    public InvalidUnionVariantException(string message) : base(message) {}

    public InvalidUnionVariantException(string message, Exception inner) : base(message, inner) {}
}