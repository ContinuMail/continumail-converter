// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Models;

public class TaskRecordTests
{
    [Fact]
    public void Defaults_are_not_started_normal_importance_no_reminder()
    {
        var t = new TaskRecord { Subject = "Buy milk" };
        Assert.Equal(TaskStatusKind.NotStarted, t.Status);
        Assert.Equal(0, t.PercentComplete);
        Assert.Equal(1, t.Importance);
        Assert.False(t.ReminderSet);
        Assert.Empty(t.Categories);
    }
}
