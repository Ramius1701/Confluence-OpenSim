// [Confluence] Tranquillity #360: a connection string's password is masked before it is logged, wherever it sits.
// Its own tests capture Microsoft.Extensions.Logging output in projects Confluence does not have; this checks the
// masking itself, which the SQLite stores now use for their "connecting" lines.

using OpenSim.Framework;
using Xunit;

namespace InWorldz.Phlox.Tests;

public class DisplayConnectionStringTests
{
    [Theory]
    [InlineData("Data Source=file:region.db;Version=3", "Data Source=file:region.db;Version=3")]
    [InlineData("Server=db;Database=opensim;User ID=os;Password=s3cret;Old Guids=true", "Server=db;Database=opensim;User ID=os;Password=***;Old Guids=true")]
    [InlineData("Server=db;User ID=os;Password=s3cret", "Server=db;User ID=os;Password=***")]           // last, no ';'
    [InlineData("Server=db;User ID=os;password=s3cret;", "Server=db;User ID=os;password=***;")]
    [InlineData("Server=db;PASSWORD = s3cret;Pooling=true", "Server=db;PASSWORD =***;Pooling=true")]
    [InlineData("Server=db;Password", "Server=db;Password")]                                             // no value at all
    public void The_password_is_never_shown(string connectionString, string shown)
    {
        string display = OpenSim.Framework.Util.GetDisplayConnectionString(connectionString);
        Assert.Equal(shown, display);
        Assert.DoesNotContain("s3cret", display);
    }
}
