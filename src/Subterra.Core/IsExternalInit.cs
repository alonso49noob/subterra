namespace System.Runtime.CompilerServices
{
    /// <summary>Lets C# 9 <c>init</c> accessors compile on net472, which does not ship this marker type.</summary>
    internal static class IsExternalInit
    {
    }
}
