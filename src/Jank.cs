using System.ComponentModel;

// this makes records usable on net4.8
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit {}
}