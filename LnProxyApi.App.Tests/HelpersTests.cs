using LnProxyApi.Helpers;


namespace LnProxyApi.App.Tests;

public class LnProxyTests
{
    [SetUp]
    public void Setup()
    {
    
    }

    [Test]
    public void HexStringHelperTest()
    {
        var bytes = HexStringHelper.HexStringToByteString("f32039b06e834b65e6b1af17fd0217100176f14a3d0e4bed4becbe5058544415");
        Assert.That(bytes.IsEmpty, Is.EqualTo(false));
        Assert.That(bytes.Length, Is.EqualTo(32));
        Assert.That(bytes.ToBase64().ToString(), Is.EqualTo("8yA5sG6DS2Xmsa8X/QIXEAF28Uo9DkvtS+y+UFhURBU="));
    }
}
