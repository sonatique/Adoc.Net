using AdocNet.Extensions;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class SigningHelperTests
{
    [Test]
    public void ToHexString_NullToken_ReturnsEmpty()
    {
        Assert.That(SigningHelper.ToHexString(null), Is.EqualTo(""));
    }

    [Test]
    public void ToHexString_EmptyToken_ReturnsEmpty()
    {
        Assert.That(SigningHelper.ToHexString(Array.Empty<byte>()), Is.EqualTo(""));
    }

    [Test]
    public void ToHexString_KnownBytes_ReturnsCorrectHex()
    {
        var bytes = new byte[] { 0xAB, 0x40, 0x02, 0x0B, 0x15, 0x1F, 0x4A, 0xAE };
        Assert.That(SigningHelper.ToHexString(bytes), Is.EqualTo("ab40020b151f4aae"));
    }

    [Test]
    public void ToHexString_AllZeros_ReturnsZeroHex()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.That(SigningHelper.ToHexString(bytes), Is.EqualTo("0000000000000000"));
    }

    [Test]
    public void IsValidTokenFormat_Valid16HexChars_ReturnsTrue()
    {
        Assert.That(SigningHelper.IsValidTokenFormat("ab40020b151f4aae"), Is.True);
    }

    [Test]
    public void IsValidTokenFormat_UpperCaseHex_ReturnsTrue()
    {
        Assert.That(SigningHelper.IsValidTokenFormat("AB40020B151F4AAE"), Is.True);
    }

    [Test]
    public void IsValidTokenFormat_TooShort_ReturnsFalse()
    {
        Assert.That(SigningHelper.IsValidTokenFormat("ab40020b"), Is.False);
    }

    [Test]
    public void IsValidTokenFormat_TooLong_ReturnsFalse()
    {
        Assert.That(SigningHelper.IsValidTokenFormat("ab40020b151f4aae00"), Is.False);
    }

    [Test]
    public void IsValidTokenFormat_NonHexChars_ReturnsFalse()
    {
        Assert.That(SigningHelper.IsValidTokenFormat("zz40020b151f4aae"), Is.False);
    }

    [Test]
    public void IsValidTokenFormat_Null_ReturnsFalse()
    {
        Assert.That(SigningHelper.IsValidTokenFormat(null!), Is.False);
    }
}
