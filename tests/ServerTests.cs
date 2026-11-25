// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Optional test against a running database server of another product that accepts applications of other
// databases and stores in PostgreSQL. Skipped without EQLMAKER_E2E_TDS_PORT (see docs/other-databases.md).
using System;
using System.IO;

namespace EqlMaker.Tests {

public static class ServerTests {
    /// <summary>A T-SQL client works against a running EQLServer while EQLMaker captures: batches and calls arrive through the statement
    /// trace, changed rows through logical decoding, the time line ties them. Details: tests/eqlserver/run-eqlserver-e2e.ps1.</summary>
    public static void TdsStatementsAndChangedRowsOfAnApplicationReachTheTimeline() {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EQLMAKER_E2E_TDS_PORT"))) throw new SkipException("EQLMAKER_E2E_TDS_PORT is not set (a running server is needed, see docs/other-databases.md)");
        string script = Path.Combine(T.RepoRoot, "tests", "eqlserver", "run-eqlserver-e2e.ps1"), o;
        int rc = T.Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"", T.RepoRoot, out o, 1500000);
        T.Eq(0, rc, "run-eqlserver-e2e.ps1:\n" + o);
        T.Contains(o, "0 failed", "summary");
    }
}
}
