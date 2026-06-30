using System;
using System.Collections.Generic;
using System.IO;
using PSTFileFormat;
using Xunit;
using Xunit.Abstractions;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// PR6 Task 2 gate: proves that MessageRecipient.ResponseStatus round-trips through
/// AddRecipient → GetRecipient (PidTagRecipientTrackStatus), and that PidLidResponseStatus
/// resolves in the named-property map.
/// </summary>
public class RecipientTrackStatusTests
{
    private readonly ITestOutputHelper _out;
    public RecipientTrackStatusTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void AddRecipient_with_response_status_round_trips_via_GetRecipient()
    {
        string path = Path.Combine(Path.GetTempPath(), $"track-status-{Guid.NewGuid():N}.pst");
        _out.WriteLine($"PST: {path}");
        try
        {
            // Build store + calendar folder + appointment with one recipient carrying ResponseStatus=3.
            PSTFile.CreateEmptyStore(path);
            PSTFile file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                file.BeginSavingChanges();
                PSTFolder cal = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);

                SingleAppointment appt = SingleAppointment.CreateNewSingleAppointment(file, cal.NodeID);
                appt.Subject = "Meeting with track status";
                appt.InternetCodepage = 65001;
                appt.SetStartAndDuration(new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc), 60);

                appt.AddRecipients(new List<MessageRecipient>
                {
                    new MessageRecipient("Req One", "req1@example.com", isOrganizer: false, RecipientType.To)
                    {
                        ResponseStatus = 3
                    }
                });

                appt.SaveChanges();
                cal.AddMessage(appt);
                cal.SaveChanges();
                file.EndSavingChanges();
            }
            finally { file.CloseFile(); }

            // Reopen and verify round-trip.
            PSTFile reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                PSTFolder found = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
                CalendarFolder calRo = Assert.IsType<CalendarFolder>(found);
                Appointment read = calRo.GetAppointment(0);
                MessageRecipient r = read.GetRecipient(0);
                Assert.Equal(3, r.ResponseStatus);
            }
            finally { reopened.CloseFile(); }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PidLidResponseStatus_name_resolves()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pidlid-resolve-{Guid.NewGuid():N}.pst");
        try
        {
            PSTFile.CreateEmptyStore(path);
            PSTFile file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                file.BeginSavingChanges();
                // Should not throw; returns a usable named-property ID.
                PropertyID id = file.NameToIDMap.ObtainIDFromName(
                    new PropertyName(PropertyLongID.PidLidResponseStatus, PropertySetGuid.PSETID_Appointment));
                Assert.True((int)id >= 0x8000, $"Expected mapped named-property ID ≥ 0x8000, got {(int)id:X}");
                file.EndSavingChanges();
            }
            finally { file.CloseFile(); }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Default_recipient_with_no_response_status_round_trips_zero()
    {
        // Backward-compat regression: a recipient added WITHOUT setting ResponseStatus (the mail path)
        // must still read back 0.
        string path = Path.Combine(Path.GetTempPath(), $"track-compat-{Guid.NewGuid():N}.pst");
        try
        {
            PSTFile.CreateEmptyStore(path);
            PSTFile file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                file.BeginSavingChanges();
                PSTFolder cal = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);

                SingleAppointment appt = SingleAppointment.CreateNewSingleAppointment(file, cal.NodeID);
                appt.Subject = "Plain meeting";
                appt.InternetCodepage = 65001;
                appt.SetStartAndDuration(new DateTime(2026, 6, 30, 11, 0, 0, DateTimeKind.Utc), 30);

                // No ResponseStatus set — default 0.
                appt.AddRecipients(new List<MessageRecipient>
                {
                    new MessageRecipient("Plain To", "to@example.com", isOrganizer: false, RecipientType.To)
                });

                appt.SaveChanges();
                cal.AddMessage(appt);
                cal.SaveChanges();
                file.EndSavingChanges();
            }
            finally { file.CloseFile(); }

            PSTFile reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                PSTFolder found = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
                CalendarFolder calRo = Assert.IsType<CalendarFolder>(found);
                Appointment read = calRo.GetAppointment(0);
                MessageRecipient r = read.GetRecipient(0);
                Assert.Equal(0, r.ResponseStatus);
            }
            finally { reopened.CloseFile(); }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
