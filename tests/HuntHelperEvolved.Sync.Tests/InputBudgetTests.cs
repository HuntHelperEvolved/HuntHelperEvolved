using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Xunit;
namespace HuntHelperEvolved.Sync.Tests;

public class InputBudgetTests
{
    private static string Code(string json)
    {
        using var bytes=new MemoryStream();
        using(var gzip=new GZipStream(bytes,CompressionMode.Compress,true))gzip.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(bytes.ToArray());
    }
    [Theory]
    [InlineData("http://127.0.0.1:8080",false)][InlineData("ws://example.com/ws",false)]
    [InlineData("https://example.com",true)][InlineData("example.com",true)]
    [InlineData("wss://username:password@example.com",false)]
    public void PlaintextRequiresExplicitOptInAndCredentialsStayOutOfUrls(string url,bool expected)
    {
        Assert.Equal(expected,SyncEndpoint.TryBuild(url,out _,out _));
        if(url.StartsWith("http://") || url.StartsWith("ws://"))
            Assert.True(SyncEndpoint.TryBuild(url,out _,out _,true));
    }
    [Fact]
    public void ParsedStringAllocationHasItsOwnBudget()
    {
        using var reader=new LimitedJsonReader(new StringReader("{\"value\":\""+new string('x',8193)+"\"}"));
        Assert.Throws<JsonReaderException>(()=>Newtonsoft.Json.Linq.JObject.Load(reader));
    }
    [Fact]
    public void SmallCompressedBombIsRejectedBeforeFullOutputAllocation()
    {
        var code=Code("[{\"Name\":\""+new string('x',8*1024*1024)+"\",\"MobID\":1}]");
        Assert.True(code.Length<20000);
        Assert.Null(TrainExchange.Import(code));
    }
    [Theory]
    [InlineData("[{\"MobID\":1},null]")]
    [InlineData("[{\"MobID\":1,\"Instance\":10}]")]
    [InlineData("[{\"MobID\":1,\"Position\":{\"X\":1000,\"Y\":10}}]")]
    public void InvalidTailAndValuesRejectTheEntireImport(string json)=>Assert.Null(TrainExchange.Import(Code(json)));
    [Fact]
    public void NamesAndRowCountAreBounded()
    {
        Assert.Null(TrainExchange.Import(Code(JsonConvert.SerializeObject(new[]{new ExchangeMob{MobID=1,Name=new string('a',129)}}))));
        Assert.Null(TrainExchange.Import(Code(JsonConvert.SerializeObject(Enumerable.Range(1,513).Select(i=>new ExchangeMob{MobID=(uint)i})))));
    }
    [Fact]
    public void PausedConsumerCannotAccumulateUnboundedQueuedBytes()
    {
        var queue=new ByteBudgetQueue<string>(128,1024);
        for(var i=0;i<4;i++)Assert.True(queue.TryEnqueue("payload",256));
        for(var i=0;i<10000;i++)Assert.False(queue.TryEnqueue("overflow",256));
        Assert.Equal(1024,queue.Bytes);
        Assert.True(queue.TryDequeue(out _));Assert.Equal(768,queue.Bytes);
        Assert.True(queue.TryEnqueue("replacement",256));
        queue.Clear();Assert.Equal(0,queue.Bytes);Assert.False(queue.TryDequeue(out _));
    }
}
