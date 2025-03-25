using System;

namespace SteamQueryNet.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class EdfAttribute : Attribute
    {
        internal EdfAttribute(byte condition) { }
    }
}
