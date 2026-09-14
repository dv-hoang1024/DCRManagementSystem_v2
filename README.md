# DCR Management System - Enterprise / Plant Edition

Windows Forms application for **Temporary Deviation Change Request / Order**.

This edition extends the Production Workflow build for day-to-day engineering use in an internal factory network: modular Wizard UI, automatic draft persistence, dynamic approval routing, Windows/AD authentication, network file storage for large engineering documents, automatic ZIP compression, PDF generation, electronic approval evidence and automatic EF Core audit trail.

## Latest update - optimized SMB upload, PDF review and PDF mail attachment

- Attachment upload to File Server/NAS is now **single-pass**: SHA-256 is calculated while bytes are copied, so the application no longer writes the file and then reads the same remote file back across SMB just to calculate its hash. Large technical files are ZIP-compressed in a local staging folder before one sequential upload to the share.
- Upload uses a 4 MB pooled buffer, a process-wide upload gate, Polly retry, and a `.uploading-*` temporary remote file that is renamed only after the copy finishes.
- Approval email subject is standardized as **`[GGP] DCR cần phê duyệt: <DCR Number>`**.
- The current DCR is exported with QuestPDF and stored under the configured file storage in `GeneratedPdf`; the central Mail Worker attaches that PDF to approval/outcome/reminder emails.
- `Review & Submit` now renders the actual exported PDF inside the WinForms screen through WebView2, with **Làm mới PDF** and **Mở PDF** fallback controls.
- Existing databases need the idempotent `Database/PdfMailAttachment_Upgrade.sql` once to add the EmailOutbox attachment metadata columns. Startup schema upgrade contains the same checks.

---

## 0. Production hardening update - concurrency, batch BOM input and network resilience

This source includes the following additional plant-operation safeguards:

- **Submit closes the DCR window automatically** after a successful workflow submission.
- **Administrator DCR deletion** is available from the Main screen. Deletion requires typing the exact DCR number. Active DCR data is removed, related physical files are cleaned up best-effort, and an immutable deletion snapshot is kept in `DCRDeletionLogs`.
- **Optimistic concurrency** uses SQL Server `rowversion` (`DCRRequests.RowVersion`). Draft saves, Submit and approval decisions compare the version that the user opened against the current database version. A conflict keeps local Draft Recovery and offers to reload the latest server copy. Every approval action touches the DCR header so parallel approvers cannot advance a stage from stale workflow data.
- **Wizard validation contract** is standardized through `UserControls/IWizardStepControl.cs` with `ValidateStepAsync`, `BindFromModel` and `SyncToModel`.
- **Part List batch input** supports Ctrl+V / Paste from Excel and `.xlsx` import through ClosedXML. Expected columns are `Change`, `Part Number`, `Part Name`, `KPC`, `Quantity`, `Replaced By`.
- **Network resilience** uses Polly v8 with three retry attempts and approximately 1.5 seconds between attempts for transient SQL/file I/O. Every service opens its SQL connection through `OpenSqlConnectionWithRetryAsync`; SQL configuration tests and startup/bootstrap use the same retry pipeline. File upload retries remove partial destination files before replaying the stream/ZIP operation. All SQL connection strings also set `ConnectRetryCount=3` and `ConnectRetryInterval=1`. Business transactions are not blindly replayed after an unknown commit result.

For an existing database, run `Database/ProductionUpgrade.sql` once before using the new Administrator delete function. The startup upgrade also adds missing `RowVersion` and `DCRDeletionLogs` when the base schema already exists.

---

## Technology

- C# / .NET 8 Windows Forms
- SQL Server / SQL Server LocalDB for development
- Entity Framework Core 8
- `System.DirectoryServices.Protocols` for optional LDAP/AD credential validation
- Windows Integrated Authentication based on the current Windows session
- QuestPDF
- SMTP notification and optional Microsoft Teams webhook
- PBKDF2-SHA256 local fallback password hashing
- HMAC-SHA256 approval signature hash
- SHA-256 attachment integrity verification
- File Server / NAS storage through Windows UNC shares

---

## 1. Wizard architecture - no DCR God Form

`DCRDetailForm.cs` is now the Wizard host. The step-specific controls are under:

```text
DCRManagementSystem/
├─ Forms/
│  ├─ DCRDetailForm.cs              # Wizard host/navigation/workflow actions
│  └─ DcrDecisionDialog.cs          # Approve/Reject/Request Info dialog
│
└─ UserControls/
   ├─ WizardControlUi.cs
   ├─ GeneralInfoStepControl.cs
   ├─ PartDetailsStepControl.cs
   ├─ DefectDescriptionStepControl.cs
   ├─ PlanTrackingStepControl.cs
   └─ ReviewSubmitStepControl.cs
```

All Wizard steps exchange state through `DcrEditModel` in `Models/EditModels.cs`. There is no `TabControl` or `TableLayoutPanel` in the Wizard.

### Five DCR steps

1. **General Information & Vehicle Program**
   - Requesting Department / Module Group
   - Owner / Created Date
   - Title
   - Program / Build Stage
   - ECR / PPS / ECN / MCN
2. **Part List**
   - Change / Part Number / Part Name / KPC / Quantity / Replaced By
3. **Problem, Solution & Documents**
   - Problem Description
   - Solution / Material Change
   - Form/Fit/Function
   - Retrofit
   - Impacted Departments
   - Technical attachments
4. **Plan & Tracking**
   - Material identification
   - Material Usage Station
   - Supplier MRD timing
   - Expected Arrival Date
   - Temporary Process / Rework
   - Planned Start / End Date
   - Production Order Number
5. **Review & Submit**
   - Full review
   - Approval history
   - Audit trail

### Validation and Draft

- `Next` validates the current step.
- Every successful `Next` saves the Draft to SQL Server.
- `Submit` validates the full DCR again in `DcrService`.
- A 30-second auto-save saves only changed Drafts.
- `DraftStep` and `LastSavedAt` are persisted in SQL Server.
- A local JSON recovery copy is written before server saves:

```text
%LOCALAPPDATA%\DCRManagementSystem\DraftRecovery\User_<id>\
```

If the SQL Server connection is temporarily unavailable, the local recovery remains and can be restored the next time the DCR is opened.

---

## 2. Workflow state machine and RBAC

Implemented lifecycle:

```text
Draft
  -> Submit
InApproval / Design Manager
  -> Impacted Department Manager(s)
  -> ME Manager
  -> Product Engineering / Head of Engineering
  -> Approved

Any active approval stage:
  -> Reject       => Rejected
  -> Request Info => Draft, Revision + 1
```

After Submit, the Initiator sees the DCR as read-only. Editing and attachment changes are enabled again only after `Request Info` returns the DCR to Draft.

The approval matrix supports:

- Requesting Department Manager
- Impacted Department Manager(s), parallel
- Target Department Manager
- Role
- Specific User
- Requesting Department conditions
- Priority and Active flags

Workflow operations use database transactions; the DCR has SQL `rowversion` for optimistic concurrency.

---

## 3. Windows Authentication / Active Directory / LDAP

Authentication settings are in `DCRManagementSystem/appsettings.json`:

```json
"Authentication": {
  "Mode": "WindowsPreferred",
  "AutoLoginWindows": true,
  "AllowLocalFallback": true,
  "AllowUsernameMatchForWindows": true,
  "AllowedWindowsDomain": "",
  "UseWindowsSessionForApproval": true,
  "Ldap": {
    "Enabled": false,
    "Host": "",
    "Port": 389,
    "UseSsl": false,
    "Domain": ""
  }
}
```

Supported modes:

```text
LocalOnly
WindowsPreferred
WindowsOnly
```

### Windows SSO mapping

Each DCR user has a `WindowsAccount` field, for example:

```text
PLANT\vuthuytrang
```

An Administrator can configure it in:

```text
Quản trị -> Người dùng -> Windows Account
```

At login, the application:

1. Reads the current Windows identity.
2. Checks the optional allowed Windows domain.
3. Looks for an active user mapped to exactly `DOMAIN\username`.
4. Optionally falls back to matching the short Windows username when `AllowUsernameMatchForWindows=true`.
5. Uses local password only as configured fallback.

A filtered unique database index prevents two DCR users from being mapped to the same non-empty Windows Account.

### LDAP credential login

When LDAP is enabled, manual username/password login first attempts LDAP bind. If LDAP authentication fails, local fallback is used only if allowed by the selected authentication mode.

No Active Directory password is stored in the DCR database.

### Approval authentication

Approve / Reject / Request Info uses the current authentication method:

- **WindowsIntegrated**: re-checks the current Windows identity; no extra password is required when `UseWindowsSessionForApproval=true`.
- **LDAP**: re-authenticates the password with LDAP.
- **InternalPassword**: verifies the PBKDF2 local fallback password when re-authentication is enabled.
- **ApplicationSession**: only allowed if password re-authentication has explicitly been disabled.

Every approval stores the authentication method, authentication timestamp, Windows identity and HMAC-SHA256 signature hash.

---

## 4. Local Server / NAS storage for engineering files

The application does **not** store CAD/images/technical documents as `byte[]` in SQL Server.

SQL Server stores only metadata such as:

```text
FilePath
OriginalFileName
StoredFileName
FileSize
StoredFileSize
FileExtension
StorageProvider
OriginalSha256Hash
Sha256Hash
IsCompressed
CompressionType
UploadedBy
UploadedAt
```

The physical file is streamed to a File Server / NAS through `FileStream`.

### Current plant default

```text
Server Address : 172.168.8.209
Folder Share   : AutoUpdate
Subfolder      : DCR
Resolved path  : \\172.168.8.209\AutoUpdate\DCR
```

These defaults are in `appsettings.json`:

```json
"Storage": {
  "UseNetworkShare": true,
  "ServerAddress": "172.168.8.209",
  "ShareName": "AutoUpdate",
  "RootSubfolder": "DCR",
  "CompressLargeTechnicalFiles": true,
  "CompressionThresholdMb": 50,
  "CompressionExtensions": ".step;.stp;.stpz;.iges;.igs;.dwg;.dxf;.sldprt;.sldasm;.catpart;.catproduct;.prt;.asm;.x_t;.x_b;.raw;.tif;.tiff"
}
```

Runtime administrators can change the shared storage configuration through:

```text
Quản trị -> Thiết lập hệ thống
```

The values are persisted centrally in `SystemSettings`, so all clients connected to the same database use the same storage destination.

The screen provides **Test kết nối**, which verifies create/write/delete permission on the selected network location.

### Windows share credentials

The application intentionally does not store a network-share username/password. Access to:

```text
\\172.168.8.209\AutoUpdate
```

uses the Windows account under which the DCR application is running. In production, grant the required users or AD groups Modify permission on both the SMB share and NTFS folder.

### File organization

Attachments are written as:

```text
\\172.168.8.209\AutoUpdate\DCR\<DCR Number>\R<Revision>\<generated-file-name>
```

Stored names are generated with GUIDs to avoid collisions. The original file name remains in SQL metadata and in the ZIP entry when compression is used.

### Automatic ZIP compression

When all conditions below are true:

- compression is enabled,
- file size is at or above `CompressionThresholdMb`,
- extension is in `CompressionExtensions`,

`AttachmentService` creates the ZIP **directly into the configured storage stream**. It does not load the full technical file into memory and it does not first create another full-size copy in SQL Server.

Both hashes are kept:

```text
OriginalSha256Hash  # original engineering file
Sha256Hash          # physical file/ZIP stored on the server
```

When a compressed attachment is opened, the application:

1. verifies the server-side ZIP SHA-256,
2. extracts into a local temporary cache,
3. verifies the extracted original SHA-256,
4. opens the verified file.

Deleted attachments are soft-deleted in the database; the physical engineering file is retained for traceability.

Environment overrides are also supported:

```text
DCR_STORAGE_SERVER
DCR_STORAGE_SHARE
```

---

## 5. Automatic Audit Trail in `AppDbContext`

Audit logging is no longer dependent only on manually calling an audit helper from each service.

`AppDbContext` overrides `SaveChanges` / `SaveChangesAsync` and inspects EF Core `ChangeTracker` for:

```text
Added
Modified
Deleted
```

It automatically writes `AuditLog` records for all tracked entities except `AuditLog` itself:

```text
AUTO INSERT
AUTO UPDATE
AUTO DELETE
```

Audit data includes:

- optional DCR Request Id
- optional User Id
- action
- entity and entity key
- changed Old Value / New Value JSON
- timestamp
- computer name
- local IP
- Windows identity
- application Session Id

Sensitive fields are redacted from automatic audit output, including password hashes and secret/password/signing-key/webhook system settings.

Business-level audit entries such as `DCR Submitted`, `DCR Returned to Draft`, `Attachment Uploaded` and approval decisions are intentionally retained alongside the automatic entity audit.

The AuditLog -> DCR foreign key uses **NO ACTION**, not cascade delete, to preserve traceability.

---

## 6. Approval signatures, PDF and notifications

Each manager decision stores an HMAC-SHA256 signature over decision-critical fields including:

```text
DCR Id
Revision
Stage
Approver
Decision
Comment
AuthenticatedAt
Windows Identity
```

The signing key must be the same on every workstation connected to the same production database. `SecurityKeyGuard` stores/checks only the key fingerprint in `SystemSettings` and blocks startup if a client presents a different key.

QuestPDF generates the DCR PDF. When final approval completes, the system automatically generates a Final Approved PDF containing approval/authentication evidence and verifies stored signatures before final generation.

Email / Teams notification and the reminder worker remain available. The reminder worker can be invoked by Windows Task Scheduler:

```text
DCRManagementSystem.exe --run-reminders
```

---

## Database upgrade

For an existing database, the application executes idempotent checks in:

```text
Data\DatabaseUpgradeService.cs
```

A DBA-readable script is included at:

```text
DCRManagementSystem\Database\ProductionUpgrade.sql
```

The upgrade includes:

- `Users.WindowsAccount`
- filtered unique Windows Account index
- attachment compression/hash/storage metadata
- nullable Audit Request/User references for configuration/system changes
- immutable AuditLog -> DCR relation (`NO ACTION`)
- existing workflow/revision/notification/system-setting production fields

For long-term production, migrate schema releases to reviewed EF Core migrations or plant DBA-managed SQL once the schema is accepted.

---

## Production deployment checklist

1. Install SQL Server / select the production database server.
2. Change `ConnectionString` from LocalDB to the plant SQL Server.
3. Run/review `Database\ProductionUpgrade.sql` through the plant DBA process.
4. Create/verify the SMB share:

   ```text
   \\172.168.8.209\AutoUpdate
   ```

5. Grant the required AD groups/users Modify permission on the SMB share and NTFS directory.
6. Open **Quản trị -> Thiết lập hệ thống** and press **Test kết nối**.
7. In **Người dùng**, map DCR users to `DOMAIN\username`.
8. Set `AllowedWindowsDomain` when the production domain name is known.
9. Disable or replace demo accounts and passwords.
10. Distribute the same protected approval signing key to every workstation.
11. Configure SMTP / Teams only on the network where those endpoints are reachable.
12. Publish Release and register `dcr://` if email deep links are used.
13. Run an end-to-end test: Draft -> Submit -> all approvals -> Final PDF -> audit verification.

---

## Requirements

- Windows 10/11
- Visual Studio 2022 with `.NET desktop development`
- .NET 8 SDK on developer machines
- .NET 8 Desktop Runtime on target machines for framework-dependent publish
- SQL Server LocalDB / Express / Standard / Enterprise
- SMB access to the configured File Server / NAS for attachment storage
- Active Directory/LDAP connectivity only when those authentication features are enabled

## First run

1. Open `DCRManagementSystem.sln`.
2. Restore NuGet packages.
3. Review `DCRManagementSystem/appsettings.json`.
4. Build -> Rebuild Solution.
5. Run and sign in.

Default development database:

```text
Server=(localdb)\MSSQLLocalDB
Database=DCRManagement
Windows Authentication
```

## Demo accounts

```text
admin            / Admin@123
initiator        / Demo@123
design.manager   / Demo@123
ga.manager       / Demo@123
me.manager       / Demo@123
chief.engineer   / Demo@123
qa.manager       / Demo@123
```

Remove or change demo credentials before production use.

## Build environment note

This source package was statically checked in the generation environment, but that environment does not contain the .NET SDK/MSBuild toolchain. A real `dotnet build` / Visual Studio Rebuild must therefore be executed on a Windows development machine before production deployment.

## SQL Server settings in the application

`Quản trị -> Thiết lập hệ thống -> SQL Server / Database` now supports configuring and testing the SQL connection before saving it for the next startup.

Default factory SQL configuration:

- Server: `172.168.8.183`
- Port: `3333`
- Database: `DCRManagement`
- Authentication: SQL Server Authentication
- Username: `admin`
- Password: configured in `appsettings.json` / local database config
- Encrypt: enabled
- Trust server certificate: enabled

The administrator can use **Test SQL** before **Lưu SQL Server**. The saved local override is written to `Data\Config\database.config.json` and is loaded before EF Core creates the first `AppDbContext`. Restart the application after changing the SQL connection.

For production deployment, restrict access to the application folder and preferably replace the shared SQL login with Windows/AD authentication or a dedicated least-privilege SQL account.

## Microsoft 365 OAuth2 email

The Email/System Settings screen now supports three authentication modes:

1. Microsoft 365 OAuth2 (recommended for Exchange Online)
2. SMTP Basic (temporary compatibility only)
3. SMTP Relay (trusted internal relay / Exchange connector)

For OAuth2 setup, see `MICROSOFT365_OAUTH2_SETUP.md` and `Scripts/Configure-DcrMicrosoft365SmtpOAuth.ps1`.
Outlook Desktop is not required; the application sends directly to Exchange Online and the mailbox can be managed through Outlook on the web.

## Microsoft Graph delegated mail

The current email implementation supports `MicrosoftGraphDelegated`, `SmtpBasic`, and `SmtpRelay`. For Graph delegated setup, read `MICROSOFT_GRAPH_DELEGATED_SETUP.md`.

## Server Mail Worker / Email Outbox

Email delivery is centralized on one server. Normal DCR clients never authenticate to Microsoft Graph and never send email directly. Workflow events enqueue rows into SQL `EmailOutbox`; the server worker sends them with the centrally signed-in Microsoft 365 mailbox.

Server commands:

```text
DCRManagementSystem.exe --mail-server-config
DCRManagementSystem.exe --run-mail-worker
DCRManagementSystem.exe --run-server-jobs
```

Run `DCRManagementSystem/Database/EmailOutbox_ServerWorker_Upgrade.sql` once on an existing database before using this build.

See `MAIL_SERVER_SETUP_AND_TEST.md` for the complete setup/test sequence.

## Dynamic per-DCR approval plan (2026-08-17)

Step 5 / Review Submit can now search active users and build a custom N-level approval plan. Multiple users in one level are parallel co-approvers; all must approve before the request advances. Leave the plan empty to use the existing Approval Matrix. See `DYNAMIC_APPROVAL_COAPPROVAL_MAIL_FIX_2026-08-17.md` and `DCRManagementSystem/Database/DynamicApprovalPlan_Upgrade.sql`.

## Organizational hierarchy: Khối -> Phòng ban

Phiên bản hiện tại hỗ trợ mô hình:

```text
Khối Sản xuất
  Director
  ├─ Phòng Cơ điện -> Manager -> Staff
  ├─ Phòng Chất lượng -> Manager -> Staff
  ├─ Phòng Sản xuất -> Manager -> Staff
  ├─ Phòng Thiết bị -> Manager -> Staff
  └─ Phòng Kỹ thuật Sản xuất -> Manager -> Staff
```

`Director`, `CTO`, `COO`, `DCEO`, `CEO` và role có HierarchyLevel từ Director trở lên thuộc **Khối**, không thuộc **Phòng ban**.

Direct Manager được tự động hóa:

- Staff -> Manager của Phòng ban.
- Manager -> Director của Khối.
- Director và cấp cao hơn -> Direct Manager cấp cao hơn do Administrator cấu hình.

Thiết lập theo thứ tự: `Quản trị -> Khối` -> `Quản trị -> Phòng ban` -> `Quản trị -> Người dùng`.
