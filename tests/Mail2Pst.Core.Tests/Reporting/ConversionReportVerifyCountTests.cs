// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Reporting;

// Locks the contract that the post-conversion verifier's expected total = all written item types.
public class ConversionReportVerifyCountTests
{
    [Fact]
    public void Expected_message_total_sums_all_item_types()
    {
        var r = new ConversionReport();
        r.RecordConverted();                 // mail
        r.RecordContactConverted();          // contact
        r.RecordAppointmentConverted();      // appointment
        r.RecordTaskConverted();             // task

        // Assert the PROPERTY the runner actually consumes, so this test fails if the runner drifts.
        Assert.Equal(4, r.TotalWrittenItems);
    }
}
