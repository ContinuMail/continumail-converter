using System; using Mail2Pst.Core.Models; using Xunit;
namespace Mail2Pst.Core.Tests.Calendar;
public class RecurrenceSpecTests
{
    [Fact] public void Defaults_have_no_recurrence_and_no_tzid()
    {
        var a = new AppointmentRecord();
        Assert.Null(a.Recurrence); Assert.Empty(a.DeletedOccurrences); Assert.Empty(a.Exceptions);
        Assert.Null(a.OriginatingTimeZoneId);
    }
    [Fact] public void InstanceId_carries_iana_id_local_and_utc()
    {
        var id = new RecurrenceInstanceId(
            new DateTime(2026,7,8,1,0,0,DateTimeKind.Utc),
            new DateTime(2026,7,8,8,0,0,DateTimeKind.Unspecified), "Asia/Bangkok", false);
        Assert.Equal("Asia/Bangkok", id.TimeZoneId);
        Assert.Equal(8, id.OriginalStartLocal.Hour);
    }
}
