using System;
using System.IO;
using PSTFileFormat;
using Xunit;
using Xunit.Abstractions;

namespace Mail2Pst.Core.Tests.Vendor;

// PR1 gate: proves the recovered MS-OXOCAL substrate can write an IPM.Appointment into a
// from-scratch CreateEmptyStore PST and read it back through the vendored reader (incl. the
// restored CalendarFolder branch in PSTFolder.GetFolder). Unicode subject locks UTF-8 fidelity.
public class AppointmentWriteSmokeTests
{
    private readonly ITestOutputHelper _out;
    public AppointmentWriteSmokeTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void SingleAppointment_RoundTrips_ThroughFromScratchStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"appt-smoke-{Guid.NewGuid():N}.pst");
        // Set MAIL2PST_KEEP_SMOKE_PST=1 to keep the file for manual Outlook / pst-validate inspection.
        bool keep = Environment.GetEnvironmentVariable("MAIL2PST_KEEP_SMOKE_PST") == "1";
        _out.WriteLine($"smoke PST: {path}");
        const string subject = "Tandlæge 🦷";   // Danish + emoji on purpose

        try
        {
            // 1. Build a fresh store, add a calendar folder, write one appointment.
            PSTFile.CreateEmptyStore(path);
            PSTFile file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                file.BeginSavingChanges();   // required before named-property allocation (appointments)
                PSTFolder cal = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);

                SingleAppointment appt = SingleAppointment.CreateNewSingleAppointment(file, cal.NodeID);
                appt.Subject = subject;
                appt.InternetCodepage = 65001;                         // UTF-8 (default 1255 Hebrew — gotcha)
                appt.SetStartAndDuration(new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc), 60);
                appt.SaveChanges();

                cal.AddMessage(appt);
                cal.SaveChanges();                                     // BEFORE EndSavingChanges (gotcha)
                file.EndSavingChanges();
            }
            finally { file.CloseFile(); }

            // 2. Reopen and assert the appointment round-trips.
            PSTFile reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                PSTFolder found = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
                CalendarFolder cal = Assert.IsType<CalendarFolder>(found);   // GetFolder calendar branch
                Assert.Equal(1, cal.AppointmentCount);
                Appointment read = cal.GetAppointment(0);
                Assert.Equal(subject, read.Subject);
            }
            finally { reopened.CloseFile(); }
        }
        finally { if (!keep && File.Exists(path)) File.Delete(path); }
    }
}
