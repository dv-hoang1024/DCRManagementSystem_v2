# DCR Management System

Windows desktop application for managing **Temporary Deviation Change Requests (DCR)** in a manufacturing environment.

The system covers DCR creation, technical attachments, approval workflow, PDF generation, audit history and email notifications.

## Tech stack

- C# / .NET 8
- Windows Forms
- Entity Framework Core 8
- SQL Server
- QuestPDF
- WebView2
- ClosedXML
- Polly
- Windows Authentication / Active Directory / LDAP
- Microsoft Graph / SMTP
- SMB / NAS file storage

## Main functions

- 5-step DCR wizard
- Draft autosave and local recovery
- Part List paste/import from Excel
- Multi-level approval workflow
- Parallel approvers at the same approval level
- Configurable Approval Matrix
- Custom approval plan per DCR
- Windows/AD login with optional LDAP or local fallback
- Technical file storage on File Server / NAS
- SHA-256 file verification
- ZIP compression for large engineering files
- PDF preview and final approved PDF
- Central Email Outbox / Mail Worker
- Automatic audit trail
- SQL Server `rowversion` concurrency control
- Organization hierarchy: Khối -> Phòng ban -> Người dùng

## Project structure

```text
DCRManagementSystem/
├─ Forms/
│  ├─ DCRDetailForm.cs
│  └─ DcrDecisionDialog.cs
├─ UserControls/
│  ├─ WizardControlUi.cs
│  ├─ GeneralInfoStepControl.cs
│  ├─ PartDetailsStepControl.cs
│  ├─ DefectDescriptionStepControl.cs
│  ├─ PlanTrackingStepControl.cs
│  └─ ReviewSubmitStepControl.cs
├─ Models/
├─ Services/
├─ Data/
├─ Database/
└─ appsettings.json
```

`DCRDetailForm` is the wizard host. Step controls work with `DcrEditModel` and implement the common wizard contract in `IWizardStepControl`.

Keep business logic in the service layer rather than adding it directly to WinForms event handlers.

## DCR flow

Default workflow:

```text
Draft
  -> Submit
In Approval
  -> Design Manager
  -> Impacted Department Manager(s)
  -> ME Manager
  -> Product Engineering / Head of Engineering
  -> Approved
```

At an active approval stage:

```text
Reject       -> Rejected
Request Info -> Draft, Revision + 1
```

Step 5 can also define a custom N-level approval plan.

If multiple approvers are assigned to one level, all of them must approve before the DCR moves to the next level.

If no custom plan is defined, the system uses the Approval Matrix.

## Wizard steps

### 1. General Information & Vehicle Program

Basic request information, owner, department, title, program/build stage and engineering references.

### 2. Part List

Main columns:

```text
Change
Part Number
Part Name
KPC
Quantity
Replaced By
```

Supports manual input, paste from Excel and `.xlsx` import.

### 3. Problem, Solution & Documents

Problem description, proposed solution, Form/Fit/Function, retrofit, impacted departments and technical attachments.

### 4. Plan & Tracking

Material information, supplier timing, arrival date, temporary process/rework, planned dates and production order.

### 5. Review & Submit

Final review, approval plan/history and PDF preview before submission.

## Draft recovery

Drafts are saved to SQL Server during editing.

A local recovery copy is also stored under:

```text
%LOCALAPPDATA%\DCRManagementSystem\DraftRecovery\User_<id>\
```

This is used when SQL Server or the network is temporarily unavailable.

## Authentication

Authentication is configured in:

```text
DCRManagementSystem/appsettings.json
```

Supported modes:

```text
LocalOnly
WindowsPreferred
WindowsOnly
```

Users can be mapped to a Windows account in this format:

```text
DOMAIN\username
```

Configuration screen:

```text
Quản trị -> Người dùng -> Windows Account
```

LDAP is optional. Active Directory passwords are not stored in the application database.

## Organization hierarchy

The current organization model is:

```text
Khối
  Director
  ├─ Phòng ban -> Manager -> Staff
  ├─ Phòng ban -> Manager -> Staff
  └─ ...
```

Direct Manager resolution:

```text
Staff   -> Manager của Phòng ban
Manager -> Director của Khối
Director and above -> configured Direct Manager
```

Recommended setup order:

```text
Quản trị -> Khối
Quản trị -> Phòng ban
Quản trị -> Người dùng
```

## File storage

Technical files are stored on File Server / NAS instead of inside SQL Server.

Typical layout:

```text
\\<server>\<share>\DCR\<DCR Number>\R<Revision>\<generated-file-name>
```

SQL Server stores file metadata and hashes.

Example configuration:

```json
"Storage": {
  "UseNetworkShare": true,
  "ServerAddress": "<file-server>",
  "ShareName": "<share-name>",
  "RootSubfolder": "DCR",
  "CompressLargeTechnicalFiles": true,
  "CompressionThresholdMb": 50
}
```

The application uses the Windows account of the running process to access the SMB share.

Do not commit production file-server credentials, internal addresses or UNC paths to the public repository.

## Audit and concurrency

`AppDbContext` automatically records tracked entity changes.

Business events such as submission, return to draft, attachment upload and approval decisions are also logged.

Sensitive fields such as passwords and signing keys must not be written to audit output.

DCR records use SQL Server `rowversion` to prevent stale clients from overwriting newer data.

## PDF and approval evidence

QuestPDF is used to generate DCR documents.

The Review & Submit screen previews the exported PDF with WebView2.

Final approval generates a PDF containing the approval history and authentication evidence.

Approval decisions are protected with an HMAC-SHA256 signature. All clients connected to the same production database must use the same protected signing key.

Do not commit the production signing key.

## Email

Desktop clients do not send workflow email directly.

They write jobs to `EmailOutbox`, and a server-side worker sends them using the configured mail account.

Supported modes:

```text
MicrosoftGraphDelegated
SmtpBasic
SmtpRelay
```

Server commands:

```text
DCRManagementSystem.exe --mail-server-config
DCRManagementSystem.exe --run-mail-worker
DCRManagementSystem.exe --run-server-jobs
DCRManagementSystem.exe --run-reminders
```

Related documents:

```text
MICROSOFT365_OAUTH2_SETUP.md
MICROSOFT_GRAPH_DELEGATED_SETUP.md
MAIL_SERVER_SETUP_AND_TEST.md
```

## Database

Development can use SQL Server LocalDB:

```text
Server=(localdb)\MSSQLLocalDB
Database=DCRManagement
Windows Authentication
```

Database upgrade scripts are under:

```text
DCRManagementSystem/Database/
```

Current upgrade scripts include:

```text
ProductionUpgrade.sql
PdfMailAttachment_Upgrade.sql
EmailOutbox_ServerWorker_Upgrade.sql
DynamicApprovalPlan_Upgrade.sql
```

Startup upgrade logic is in:

```text
Data/DatabaseUpgradeService.cs
```

Review database changes before running them against an existing environment.

## Development setup

Requirements:

- Windows 10/11
- Visual Studio 2022
- `.NET desktop development` workload
- .NET 8 SDK
- SQL Server LocalDB / Express / Standard / Enterprise
- WebView2 Runtime

Run locally:

1. Clone the repository.
2. Open `DCRManagementSystem.sln`.
3. Restore NuGet packages.
4. Review `DCRManagementSystem/appsettings.json`.
5. Configure a development database.
6. Build the solution.
7. Run the application.

Use development-safe configuration only. Do not point a local build at the production database, production mail account or production file share unless that is intentional.

## Notes for contributors

When changing the wizard:

- keep step UI and validation inside the corresponding `UserControl`
- exchange wizard data through `DcrEditModel`
- keep `IWizardStepControl` behavior consistent

When changing workflow:

- keep state transitions in the service layer
- preserve database transactions
- preserve `rowversion` concurrency checks
- test both Approval Matrix and custom approval plans

When changing attachments:

- do not load large engineering files fully into memory
- preserve SHA-256 verification
- make sure failed uploads cannot leave a valid-looking attachment

When changing entities or database schema:

- check automatic audit behavior
- do not expose secrets in audit data
- add an upgrade path for existing databases

When changing email:

- desktop clients should enqueue email
- mail authentication should remain on the server worker

## Before making the repository public

Check at least:

```text
appsettings.json
Data/Config/
launchSettings.json
Database/
Scripts/
documentation
sample data
```

Remove or replace:

- passwords
- access tokens
- signing keys
- internal IP addresses
- private hostnames
- internal UNC paths
- real employee usernames
- real email addresses
- production connection strings
- demo credentials that should not be public

Use placeholders and local/environment-specific configuration for private infrastructure.

## License

Add a license before distributing the repository outside the organization.
