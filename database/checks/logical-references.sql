-- 回傳非 NULL 但父資料不存在的邏輯參照。不會修復或刪除資料。
-- NULL 可能是尚未完成的資料；完成狀態另由 CHECK 與未來的應用程式服務驗證。
SELECT 'CaseRevision:' || r.CaseRevisionID AS Issue
FROM CaseRevision r LEFT JOIN NamingCase p ON p.NamingCaseID = r.forNamingCaseID
WHERE r.forNamingCaseID IS NOT NULL AND p.NamingCaseID IS NULL
UNION ALL
SELECT 'CaseClarification:' || r.CaseClarificationID
FROM CaseClarification r LEFT JOIN CaseRevision p ON p.CaseRevisionID = r.forCaseRevisionID
WHERE r.forCaseRevisionID IS NOT NULL AND p.CaseRevisionID IS NULL
UNION ALL
SELECT 'ProcessingRun:' || r.ProcessingRunID
FROM ProcessingRun r LEFT JOIN CaseRevision p ON p.CaseRevisionID = r.forCaseRevisionID
WHERE r.forCaseRevisionID IS NOT NULL AND p.CaseRevisionID IS NULL
UNION ALL
SELECT 'ProcessingStep:' || r.ProcessingStepID
FROM ProcessingStep r LEFT JOIN ProcessingRun p ON p.ProcessingRunID = r.forProcessingRunID
WHERE r.forProcessingRunID IS NOT NULL AND p.ProcessingRunID IS NULL
UNION ALL
SELECT 'ExecutionCall:' || r.ExecutionCallID
FROM ExecutionCall r LEFT JOIN ProcessingStep p ON p.ProcessingStepID = r.forProcessingStepID
WHERE r.forProcessingStepID IS NOT NULL AND p.ProcessingStepID IS NULL
UNION ALL
SELECT 'NamingReport:' || r.NamingReportID
FROM NamingReport r LEFT JOIN ProcessingRun p ON p.ProcessingRunID = r.forProcessingRunID
WHERE r.forProcessingRunID IS NOT NULL AND p.ProcessingRunID IS NULL
UNION ALL
SELECT 'ReportArtifact:' || r.ReportArtifactID
FROM ReportArtifact r LEFT JOIN NamingReport p ON p.NamingReportID = r.forNamingReportID
WHERE r.forNamingReportID IS NOT NULL AND p.NamingReportID IS NULL;
