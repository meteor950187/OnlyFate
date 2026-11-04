-- Schema v1. SQL 與欄位註解是資料定義的權威來源。
-- 所有 for*ID 都是邏輯關聯，不建立 physical FK 或模擬 FK 的 trigger。
-- 非 PK 欄位允許 NULL；狀態轉換的基本完整性另用 CHECK，業務完整性由 C# 驗證。
-- 系統時間為 UTC TEXT：yyyy-MM-ddTHH:mm:ss.fffZ；出生日期／時間維持出生地民用時間。

-- 案件主檔。State 只控制啟停，不代表執行階段。
CREATE TABLE NamingCase (
    NamingCaseID INTEGER PRIMARY KEY,
    Title TEXT, -- 商家辨識案件的標題，不必填客戶姓名。
    State INTEGER DEFAULT 0 CHECK (State IN (0, 1)), -- 0=正常／啟用；1=停用。
    CreatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')), -- 建立時間。
    UpdatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')) -- 應用程式負責後續更新。
) STRICT;

-- Step 1 的一個輸入版本。已供流程使用後不可原地改寫；修正產生新版本。
CREATE TABLE CaseRevision (
    CaseRevisionID INTEGER PRIMARY KEY,
    forNamingCaseID INTEGER, -- 所屬案件。
    RevisionNumber INTEGER DEFAULT 1 CHECK (RevisionNumber > 0), -- 同案件內的版本號。
    Surname TEXT CHECK (Surname IS NULL OR length(trim(Surname)) = 1), -- 第一版只支援單字姓。
    Gender TEXT CHECK (Gender IN ('male', 'female')), -- 使用者選擇，不能由名字推測。
    BirthDate TEXT CHECK (BirthDate IS NULL OR (
        BirthDate GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'
        AND BirthDate >= '0001-01-01' AND date(BirthDate, '+0 days') IS NOT NULL
        AND date(BirthDate, '+0 days') = BirthDate)), -- 原始國曆 YYYY-MM-DD，不接受不存在的日期。
    BirthTimeLocal TEXT CHECK (BirthTimeLocal IS NULL OR (
        BirthTimeLocal GLOB '[0-9][0-9]:[0-9][0-9]'
        AND substr(BirthTimeLocal, 1, 2) BETWEEN '00' AND '23'
        AND substr(BirthTimeLocal, 4, 2) BETWEEN '00' AND '59')), -- 當地 HH:mm；不補造秒數。
    BirthCountry TEXT, -- 出生國家／地區。
    BirthCity TEXT, -- 出生城市。
    BirthLocationDetail TEXT, -- 選填醫院、地址或地標。
    BirthTimeZoneID TEXT, -- 系統解析的 IANA time zone，例如 Asia/Taipei；非使用者必填欄位。
    LocationDataJson TEXT CHECK (LocationDataJson IS NULL OR CASE WHEN json_valid(LocationDataJson) THEN json_type(LocationDataJson) = 'object' ELSE 0 END), -- 經緯度、精度、時區解析依據及來源。
    CalendarDataJson TEXT CHECK (CalendarDataJson IS NULL OR CASE WHEN json_valid(CalendarDataJson) THEN json_type(CalendarDataJson) = 'object' ELSE 0 END), -- 系統換算的農曆／閏月、原始民用日期基準與來源；不混入真太陽時生日。
    AvoidanceStatus TEXT DEFAULT 'unprovided' CHECK (AvoidanceStatus IN ('unprovided', 'none', 'provided')), -- 未提供／明確沒有／有指定。
    AvoidCharactersJson TEXT CHECK (AvoidCharactersJson IS NULL OR CASE WHEN json_valid(AvoidCharactersJson) THEN json_type(AvoidCharactersJson) = 'array' ELSE 0 END), -- 禁字陣列，不收父母長輩完整姓名。
    AvoidMatchMode TEXT CHECK (AvoidMatchMode IN ('same_character', 'same_sound_tone')), -- 同字／國語同音同聲調。
    RequiredCharacterStatus TEXT DEFAULT 'unprovided' CHECK (RequiredCharacterStatus IN ('unprovided', 'none', 'provided')), -- 指定字／輩分字是否已確認。
    RequiredCharactersJson TEXT CHECK (RequiredCharactersJson IS NULL OR CASE WHEN json_valid(RequiredCharactersJson) THEN json_type(RequiredCharactersJson) = 'array' ELSE 0 END), -- 元素記錄 character、position（first／last／either）。
    Remarks TEXT, -- 使用者的彈性需求原文。
    PreferencesJson TEXT CHECK (PreferencesJson IS NULL OR CASE WHEN json_valid(PreferencesJson) THEN json_type(PreferencesJson) = 'array' ELSE 0 END), -- 風格／寓意偏好及 sourceText，不把模型取捨冒充原始要求。
    RawInputJson TEXT CHECK (RawInputJson IS NULL OR CASE WHEN json_valid(RawInputJson) THEN json_type(RawInputJson) = 'object' ELSE 0 END), -- 原始表單與本版納入的確認問答。
    OrganizedContextJson TEXT CHECK (OrganizedContextJson IS NULL OR CASE WHEN json_valid(OrganizedContextJson) THEN json_type(OrganizedContextJson) = 'object' ELSE 0 END), -- 本版已整理、可傳給後續步驟的案件 context。
    PendingIssuesJson TEXT CHECK (PendingIssuesJson IS NULL OR CASE WHEN json_valid(PendingIssuesJson) THEN json_type(PendingIssuesJson) = 'array' ELSE 0 END), -- 缺漏／矛盾／待補問；未知用 NULL，確認無問題用 []。
    Status TEXT DEFAULT 'needs_input' CHECK (Status IN ('needs_input', 'needs_correction', 'ready')), -- 等待補充／輸入需修正／資料就緒。
    ValidatedAtUtc TEXT, -- 程式完成本版資料檢查的 UTC 時間。
    CreatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CHECK (AvoidanceStatus <> 'provided' OR (AvoidCharactersJson IS NOT NULL AND AvoidMatchMode IS NOT NULL)),
    CHECK (RequiredCharacterStatus <> 'provided' OR RequiredCharactersJson IS NOT NULL),
    CHECK (Status <> 'ready' OR (
        forNamingCaseID IS NOT NULL AND RevisionNumber IS NOT NULL
        AND Surname IS NOT NULL AND Gender IS NOT NULL AND BirthDate IS NOT NULL
        AND BirthTimeLocal IS NOT NULL AND length(trim(BirthCountry)) > 0 AND BirthCountry IS NOT NULL
        AND length(trim(BirthCity)) > 0 AND BirthCity IS NOT NULL
        AND CalendarDataJson IS NOT NULL AND OrganizedContextJson IS NOT NULL
        AND ValidatedAtUtc IS NOT NULL AND PendingIssuesJson IS NOT NULL
        AND CASE WHEN json_valid(PendingIssuesJson) THEN json_array_length(PendingIssuesJson) = 0 ELSE 0 END))
) STRICT;

-- 補問及答覆。答覆納入下一版 RawInputJson，不原地改寫已使用的案件版本。
CREATE TABLE CaseClarification (
    CaseClarificationID INTEGER PRIMARY KEY,
    forCaseRevisionID INTEGER, -- 補問針對的輸入版本。
    FieldKey TEXT, -- 問題涉及的欄位；彈性語意問題可不填。
    QuestionText TEXT, -- 送給商家的精簡補問。
    AnswerText TEXT, -- 商家答覆原文。
    SourceText TEXT, -- 問題依據的原始需求片段。
    Status TEXT DEFAULT 'pending' CHECK (Status IN ('pending', 'answered', 'cancelled')),
    AskedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    AnsweredAtUtc TEXT,
    CHECK (Status <> 'answered' OR (AnswerText IS NOT NULL AND AnsweredAtUtc IS NOT NULL))
) STRICT;

-- 一次處理流程綁定一個案件版本；重新採不同輸入／規則時另開 run。
CREATE TABLE ProcessingRun (
    ProcessingRunID INTEGER PRIMARY KEY,
    forCaseRevisionID INTEGER, -- 固定輸入版本，不指向可變主檔。
    RuleSetVersion TEXT, -- 實際採用的命名規則版本；尚未選定不填，不能假定方法已驗證。
    PolicyJson TEXT CHECK (PolicyJson IS NULL OR CASE WHEN json_valid(PolicyJson) THEN json_type(PolicyJson) = 'object' ELSE 0 END), -- 本次排盤／流程政策快照。
    Status TEXT DEFAULT 'pending' CHECK (Status IN ('pending', 'running', 'waiting', 'completed', 'failed', 'cancelled')),
    StartedAtUtc TEXT,
    FinishedAtUtc TEXT,
    ErrorCode TEXT, -- 可分類且不含秘密值的錯誤代碼。
    ErrorMessage TEXT, -- 安全的錯誤摘要，不儲存 headers／API key。
    CreatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
) STRICT;

-- 六步流程的每次階段嘗試；重試增加 AttemptNumber，不能覆蓋舊次結果。
CREATE TABLE ProcessingStep (
    ProcessingStepID INTEGER PRIMARY KEY,
    forProcessingRunID INTEGER,
    StepNumber INTEGER CHECK (StepNumber BETWEEN 1 AND 6), -- 1整理、2排盤、3判讀、4組名、5檢查、6交付。
    AttemptNumber INTEGER DEFAULT 1 CHECK (AttemptNumber > 0),
    InputJson TEXT CHECK (InputJson IS NULL OR CASE WHEN json_valid(InputJson) THEN json_type(InputJson) = 'object' ELSE 0 END), -- 該次階段輸入，不是累積整段聊天。
    OutputJson TEXT CHECK (OutputJson IS NULL OR CASE WHEN json_valid(OutputJson) THEN json_type(OutputJson) = 'object' ELSE 0 END), -- 計算／判讀／選名結果；附來源與工具／模型／假設分類。
    ValidationJson TEXT CHECK (ValidationJson IS NULL OR CASE WHEN json_valid(ValidationJson) THEN json_type(ValidationJson) = 'object' ELSE 0 END), -- 檢查、拒絕與修正依據。
    Status TEXT DEFAULT 'pending' CHECK (Status IN ('pending', 'running', 'waiting', 'completed', 'failed', 'skipped', 'cancelled')),
    StartedAtUtc TEXT,
    FinishedAtUtc TEXT,
    CreatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
) STRICT;

-- 一次模型或工具呼叫的執行證據；純程式步驟不必捏造模型呼叫。
CREATE TABLE ExecutionCall (
    ExecutionCallID INTEGER PRIMARY KEY,
    forProcessingStepID INTEGER,
    CallNumber INTEGER DEFAULT 1 CHECK (CallNumber > 0), -- 該次階段嘗試中的呼叫順序。
    CallKind TEXT CHECK (CallKind IN ('model', 'tool')), -- 分開模型判讀與工具計算。
    Provider TEXT, -- 實際服務／工具提供者，例如 OpenAI／OpenFate，不預設。
    Operation TEXT, -- 實際 API 或 MCP 操作名稱。
    ModelName TEXT, -- 模型呼叫填實際模型名稱。
    ToolName TEXT, -- 工具呼叫填實際工具名稱。
    ToolVersion TEXT, -- 實際工具版本，未知保持 NULL。
    PromptVersion TEXT, -- 實際採用的指令版本。
    RequestJson TEXT CHECK (RequestJson IS NULL OR CASE WHEN json_valid(RequestJson) THEN json_type(RequestJson) = 'object' ELSE 0 END), -- 經篩選的 request body／context，不含認證資訊。
    ResponseJson TEXT CHECK (ResponseJson IS NULL OR CASE WHEN json_valid(ResponseJson) THEN json_type(ResponseJson) = 'object' ELSE 0 END), -- 實際回傳；不是模型內部思考逐字稿。
    ProviderRequestID TEXT, -- 上游追蹤 ID，不是 API key。
    InputTokens INTEGER CHECK (InputTokens >= 0), -- 實際 usage；未知 NULL，不能以 0 代替未知。
    OutputTokens INTEGER CHECK (OutputTokens >= 0),
    DurationMilliseconds INTEGER CHECK (DurationMilliseconds >= 0),
    Status TEXT DEFAULT 'running' CHECK (Status IN ('running', 'succeeded', 'failed', 'cancelled')),
    ErrorCode TEXT,
    ErrorMessage TEXT,
    StartedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    FinishedAtUtc TEXT,
    CHECK (CallKind <> 'model' OR ModelName IS NOT NULL),
    CHECK (CallKind <> 'tool' OR ToolName IS NOT NULL)
) STRICT;

-- 同一份固定報告資料供 PAGE／PDF 使用；修改內容或版型須另立報告版本。
CREATE TABLE NamingReport (
    NamingReportID INTEGER PRIMARY KEY,
    forProcessingRunID INTEGER,
    VersionNumber INTEGER DEFAULT 1 CHECK (VersionNumber > 0), -- 同 run 中的報告版本。
    ReportDataJson TEXT CHECK (ReportDataJson IS NULL OR CASE WHEN json_valid(ReportDataJson) THEN json_type(ReportDataJson) = 'object' ELSE 0 END), -- 固定報告資料含姓名、配置、配字與依據，不只有渲染文字。
    TemplateVersion TEXT, -- PAGE／PDF 共用版型版本。
    ContentSha256 TEXT CHECK (ContentSha256 IS NULL OR (length(ContentSha256) = 64 AND ContentSha256 NOT GLOB '*[^0-9a-f]*')), -- 固定 ReportDataJson UTF-8 位元組的 SHA-256 小寫 hex。
    ValidationJson TEXT CHECK (ValidationJson IS NULL OR CASE WHEN json_valid(ValidationJson) THEN json_type(ValidationJson) = 'object' ELSE 0 END), -- 報告一致性與可交付檢查結果。
    Status TEXT DEFAULT 'building' CHECK (Status IN ('building', 'ready', 'failed')), -- building 是系統製作狀態，不是交給命理老師審稿。
    ValidatedAtUtc TEXT,
    CreatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CHECK (Status <> 'ready' OR (forProcessingRunID IS NOT NULL AND VersionNumber IS NOT NULL
        AND ReportDataJson IS NOT NULL AND TemplateVersion IS NOT NULL AND ContentSha256 IS NOT NULL
        AND ValidationJson IS NOT NULL AND ValidatedAtUtc IS NOT NULL))
) STRICT;

-- 交付產物位置；資料庫不存 PDF BLOB，也不自行決定公開分享網址。
CREATE TABLE ReportArtifact (
    ReportArtifactID INTEGER PRIMARY KEY,
    forNamingReportID INTEGER,
    Format TEXT CHECK (Format IN ('html', 'pdf')),
    FilePath TEXT, -- 相對於本機報告根目錄的路徑；應用程式須驗證未逃逸該目錄。
    ContentSha256 TEXT CHECK (ContentSha256 IS NULL OR (length(ContentSha256) = 64 AND ContentSha256 NOT GLOB '*[^0-9a-f]*')), -- 產物實際位元組的 SHA-256，不同於報告資料 hash。
    FileSizeBytes INTEGER CHECK (FileSizeBytes >= 0),
    Status TEXT DEFAULT 'pending' CHECK (Status IN ('pending', 'ready', 'failed')),
    ErrorMessage TEXT,
    CreatedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now')),
    CompletedAtUtc TEXT,
    CHECK (Status <> 'ready' OR (forNamingReportID IS NOT NULL AND Format IS NOT NULL
        AND FilePath IS NOT NULL AND ContentSha256 IS NOT NULL AND FileSizeBytes IS NOT NULL AND CompletedAtUtc IS NOT NULL))
) STRICT;

-- 技術版本紀錄。版本號也寫入 PRAGMA user_version，兩者由初始化交易一起提交。
CREATE TABLE SchemaMigration (
    SchemaMigrationID INTEGER PRIMARY KEY, -- migration 版本，本版為 1。
    ScriptName TEXT, -- 固定 SQL 腳本名稱。
    ScriptSha256 TEXT CHECK (ScriptSha256 IS NULL OR (length(ScriptSha256) = 64 AND ScriptSha256 NOT GLOB '*[^0-9a-f]*')), -- 腳本 SHA-256，小寫 hex。
    AppliedAtUtc TEXT DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
) STRICT;

-- 按實際讀取路徑建立索引；不為每個邏輯參照一律建索引。
CREATE INDEX IX_NamingCase_UpdatedAtUtc ON NamingCase (UpdatedAtUtc DESC) WHERE State = 0;
CREATE UNIQUE INDEX UX_CaseRevision_CaseVersion ON CaseRevision (forNamingCaseID, RevisionNumber)
    WHERE forNamingCaseID IS NOT NULL AND RevisionNumber IS NOT NULL;
CREATE INDEX IX_CaseClarification_RevisionStatus ON CaseClarification (forCaseRevisionID, Status);
CREATE INDEX IX_ProcessingRun_RevisionCreatedAtUtc ON ProcessingRun (forCaseRevisionID, CreatedAtUtc DESC);
CREATE UNIQUE INDEX UX_ProcessingStep_RunStepAttempt ON ProcessingStep (forProcessingRunID, StepNumber, AttemptNumber)
    WHERE forProcessingRunID IS NOT NULL AND StepNumber IS NOT NULL AND AttemptNumber IS NOT NULL;
CREATE UNIQUE INDEX UX_ExecutionCall_StepCallNumber ON ExecutionCall (forProcessingStepID, CallNumber)
    WHERE forProcessingStepID IS NOT NULL AND CallNumber IS NOT NULL;
CREATE UNIQUE INDEX UX_NamingReport_RunVersion ON NamingReport (forProcessingRunID, VersionNumber)
    WHERE forProcessingRunID IS NOT NULL AND VersionNumber IS NOT NULL;
CREATE UNIQUE INDEX UX_ReportArtifact_ReportFormat ON ReportArtifact (forNamingReportID, Format)
    WHERE forNamingReportID IS NOT NULL AND Format IS NOT NULL;
CREATE UNIQUE INDEX UX_SchemaMigration_ScriptName ON SchemaMigration (ScriptName) WHERE ScriptName IS NOT NULL;
