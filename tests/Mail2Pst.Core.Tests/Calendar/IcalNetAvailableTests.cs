using System.Linq;
using Ical.Net;
using IcalCalendar = Ical.Net.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class IcalNetAvailableTests
{
    [Fact]
    public void IcalNet_loads_a_minimal_vevent()
    {
        var cal = IcalCalendar.Load("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//x//x//EN\r\n" +
            "BEGIN:VEVENT\r\nUID:1\r\nSUMMARY:Hi\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
        Assert.Equal("Hi", cal.Events.Single().Summary);
    }
}
