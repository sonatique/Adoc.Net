namespace AdocNet.TestEmptyExtension;

/// <summary>
/// A plain class that does not implement any processor interface.
/// Used to test loading a DLL that has no extension types.
/// </summary>
public sealed class EmptyClass
{
    public string Name => "I am not an extension";
}
