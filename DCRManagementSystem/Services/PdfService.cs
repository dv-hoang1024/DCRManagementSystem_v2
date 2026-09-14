using System.Data;
using System.Security.Cryptography;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DCRManagementSystem.Services;

public sealed record PdfMailAttachment(string FilePath, string FileName, string ContentType, string Sha256, long FileSize);

public sealed class PdfService : IPdfService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly StorageConfigurationService _storage;

    public PdfService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _storage = new StorageConfigurationService(dbFactory, settings);
    }

    public async Task ExportAsync(int requestId, string outputPath)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var request = await db.DCRRequests
            .AsNoTracking()
            .Include(x => x.RequestOwner)
            .Include(x => x.RequestingDepartment)
            .Include(x => x.Parts)
            .Include(x => x.ImpactedDepartments)
                .ThenInclude(x => x.Department)
            .Include(x => x.ApprovalFlow)
                .ThenInclude(x => x.Approver)
            .Include(x => x.ApprovalFlow)
                .ThenInclude(x => x.Department)
            .Include(x => x.ApprovalPlan)
                .ThenInclude(x => x.Approver)
                    .ThenInclude(x => x.Department)
            .Include(x => x.Attachments)
                .ThenInclude(x => x.Uploader)
            .SingleOrDefaultAsync(x => x.Id == requestId)
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");

        var owner = request.RequestOwner;
        var department = request.RequestingDepartment;
        var signatureVerifier = new ApprovalSignatureService(_settings);

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header()
                    .PaddingBottom(10)
                    .Column(header =>
                    {
                        header.Item()
                            .AlignCenter()
                            .Text("Temporary Deviation Change Request / Order")
                            .FontSize(16)
                            .SemiBold();

                        header.Item()
                            .PaddingTop(2)
                            .AlignCenter()
                            .Text(request.DCRNumber)
                            .FontSize(11)
                            .SemiBold();
                    });

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Element(SectionTitle).Text("Initiator Information");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                        });

                        LabelValueRow(table, "DCR #", request.DCRNumber, "Created Date", FormatDate(request.CreatedDate));
                        LabelValueRow(table, "Requesting Department / Module Group",
                            $"{department?.DepartmentCode ?? ""} / {request.ModuleGroup}",
                            "Request Owner",
                            owner?.FullName ?? "");
                        LabelValueRow(table, "Email", owner?.Email ?? "", "Cell Phone", owner?.Phone ?? "");
                    });

                    column.Item().Element(SectionTitle).Text("Request Information");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(4);
                        });

                        TwoColumnRow(table, "Title", request.Title);
                    });

                    column.Item().Element(SectionTitle).Text("Change Parts");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.0f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(3.6f);
                            columns.RelativeColumn(1.0f);
                            columns.RelativeColumn(1.5f);
                        });

                        HeaderCell(table, "Change Type");
                        HeaderCell(table, "Part Number");
                        HeaderCell(table, "Part Name");
                        HeaderCell(table, "Quantity");
                        HeaderCell(table, "Replaced by");

                        foreach (var part in request.Parts.OrderBy(x => x.SortOrder))
                        {
                            BodyCell(table, part.ChangeType);
                            BodyCell(table, part.PartNumber);
                            BodyCell(table, part.PartName);
                            BodyCell(table, part.Quantity);
                            BodyCell(table, part.ReplacedBy);
                        }

                        if (request.Parts.Count == 0)
                        {
                            table.Cell().ColumnSpan(5).Element(Cell).Text("No parts.");
                        }
                    });

                    column.Item().Element(SectionTitle).Text("1. Product Line");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                        });

                        LabelValueRow(table, "Product Line", request.Program, "Build Stage", request.BuildStage);
                        LabelValueRow(table, "Related ECR #", request.RelatedECR, "Related ECN #", request.RelatedECN);
                        LabelValueRow(table, "Related MCN #", request.RelatedMCN, string.Empty, string.Empty);
                    });

                    column.Item().Element(SectionTitle).Text("2. Impacted department(s)");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1.2f);
                        });

                        HeaderCell(table, "Departments");
                        HeaderCell(table, "Estimated Cost (If any)");

                        foreach (var impact in request.ImpactedDepartments.OrderBy(x => x.DepartmentId))
                        {
                            BodyCell(table,
                                $"{impact.Department?.DepartmentCode ?? ""} - {impact.Department?.DepartmentName ?? ""}".Trim(' ', '-'));
                            BodyCell(table, impact.EstimatedCost?.ToString("N2") ?? "-");
                        }

                        if (request.ImpactedDepartments.Count == 0)
                        {
                            table.Cell().ColumnSpan(2).Element(Cell).Text("No impacted departments.");
                        }
                    });

                    column.Item().Element(SectionTitle).Text("Attachments");
                    var attachments = request.Attachments
                        .Where(x => !x.IsDeleted)
                        .OrderBy(x => x.UploadedAt)
                        .ToList();

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1.4f);
                        });

                        HeaderCell(table, "Type");
                        HeaderCell(table, "Name");
                        HeaderCell(table, "Size");
                        HeaderCell(table, "Modified By");

                        foreach (var attachment in attachments)
                        {
                            BodyCell(table, attachment.AttachmentType);
                            BodyCell(table, attachment.FileName);
                            BodyCell(table, FormatBytes(attachment.FileSize));
                            BodyCell(table, attachment.Uploader?.FullName ?? "");
                        }

                        if (attachments.Count == 0)
                        {
                            table.Cell().ColumnSpan(4).Element(Cell).Text("There are no files.");
                        }
                    });

                    column.Item().Element(SectionTitle).Text("3. Deviation description");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(4);
                        });

                        TwoColumnRow(table, "Problem Description", request.ProblemDescription);
                        TwoColumnRow(table, "Solution", request.Solution);
                        TwoColumnRow(table, "Material Change", request.MaterialChangeDescription);
                        TwoColumnRow(table,
                            "If Parts that are subject to this change do NOT fully meet form/fit/function requirement, please fill detail",
                            request.FormFitFunctionDetail);
                        TwoColumnRow(table, "Retrofit Volume", request.RetrofitVolume);
                        TwoColumnRow(table, "Retrofit Instruction", request.RetrofitInstruction);
                    });

                    column.Item().Element(SectionTitle).Text("4. Identifications and tracking");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(4);
                        });

                        TwoColumnRow(table,
                            "DCR material must be identified with two material labels on adjacent corners of packaging",
                            YesNo(request.MaterialIdentificationRequired));
                        TwoColumnRow(table, "Station for Material Usage", request.MaterialUsageStation);
                    });

                    column.Item().Element(SectionTitle)
                        .Text("5. DCR originator has confirmed that supplier can support the MRD timing");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                        });

                        LabelValueRow(table,
                            "Supplier supports MRD timing",
                            YesNo(request.SupplierSupportsMRD),
                            "Expected Arrival Date",
                            FormatDate(request.ExpectedArrivalDate));
                    });

                    column.Item().Element(SectionTitle)
                        .Text("6. If temporary process needed, requestor can attach any supporting documents");
                    column.Item().Element(Cell)
                        .Text($"Temporary process needed: {YesNo(request.TemporaryProcessRequired)}");

                    column.Item().Element(SectionTitle).Text("7. Rework");
                    column.Item().Element(Cell)
                        .Text($"Rework on the part needed?: {YesNo(request.ReworkRequired)}");

                    column.Item().Element(SectionTitle).Text("8. Timing & break point control");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                            columns.RelativeColumn();
                            columns.RelativeColumn(2);
                        });

                        LabelValueRow(table,
                            "Planned Start Date",
                            FormatDate(request.PlannedStartDate),
                            "Planned End Date",
                            FormatDate(request.PlannedEndDate));

                        table.Cell().Element(LabelCell).Text("Production Order Number");
                        table.Cell().ColumnSpan(3).Element(Cell).Text(request.ProductionOrderNumber ?? "");
                    });

                    column.Item().PageBreak();

                    column.Item().Element(SectionTitle).Text("Current Processing Stage");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24);
                            columns.RelativeColumn(2.5f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(2.5f);
                        });

                        HeaderCell(table, "#");
                        HeaderCell(table, "Stage");
                        HeaderCell(table, "Name");
                        HeaderCell(table, "Decision");
                        HeaderCell(table, "Date");
                        HeaderCell(table, "Comments");

                        foreach (var flow in request.ApprovalFlow
                                     .OrderBy(x => x.RevisionNo)
                                     .ThenBy(x => x.StageNumber)
                                     .ThenBy(x => x.Sequence))
                        {
                            BodyCell(table, $"R{flow.RevisionNo}-{flow.StageNumber}");
                            BodyCell(table, flow.StageName);
                            BodyCell(table,
                                string.IsNullOrWhiteSpace(flow.Department?.DepartmentCode)
                                    ? flow.Approver?.FullName ?? "-"
                                    : $"{flow.Approver?.FullName ?? "-"} ({flow.Department.DepartmentCode})");
                            BodyCell(table, flow.Decision);
                            BodyCell(table, FormatDate(flow.DecisionDate));
                            var signatureStatus = string.IsNullOrWhiteSpace(flow.SignatureHash)
                                ? string.Empty
                                : flow.ApproverId.HasValue && flow.AuthenticatedAt.HasValue &&
                                  signatureVerifier.Verify(
                                      flow.RequestId,
                                      flow.RevisionNo,
                                      flow.StageNumber,
                                      flow.ApproverId.Value,
                                      flow.Decision,
                                      flow.Comments,
                                      flow.AuthenticatedAt.Value,
                                      flow.WindowsIdentity,
                                      flow.SignatureHash)
                                    ? "VERIFIED"
                                    : "INVALID";
                            var signatureInfo = string.IsNullOrWhiteSpace(flow.SignatureHash)
                                ? flow.Comments
                                : $"{flow.Comments}\nAuth: {flow.AuthMethod} @ {FormatDateTime(flow.AuthenticatedAt)}; Signature: {signatureStatus} {flow.SignatureHash[..Math.Min(16, flow.SignatureHash.Length)]}...";
                            BodyCell(table, signatureInfo);
                        }

                        if (request.ApprovalFlow.Count == 0 && request.ApprovalPlan.Count > 0)
                        {
                            foreach (var plan in request.ApprovalPlan
                                         .OrderBy(x => x.LevelNumber)
                                         .ThenBy(x => x.Sequence))
                            {
                                BodyCell(table, $"PLAN-{plan.LevelNumber}");
                                BodyCell(table, string.IsNullOrWhiteSpace(plan.LevelName) ? $"Cấp phê duyệt {plan.LevelNumber}" : plan.LevelName);
                                BodyCell(table,
                                    string.IsNullOrWhiteSpace(plan.Approver?.Department?.DepartmentCode)
                                        ? plan.Approver?.FullName ?? "-"
                                        : $"{plan.Approver?.FullName ?? "-"} ({plan.Approver.Department.DepartmentCode})");
                                BodyCell(table, "Planned");
                                BodyCell(table, "-");
                                BodyCell(table, "Approver đã được người tạo DCR chọn trước khi Submit.");
                            }
                        }
                        else if (request.ApprovalFlow.Count == 0)
                        {
                            table.Cell().ColumnSpan(6).Element(Cell).Text("Workflow has not been submitted. Approval Matrix will be used when the DCR is submitted.");
                        }
                    });

                    column.Item().PaddingTop(8).Text(text =>
                    {
                        text.Span("Status: ").SemiBold();
                        text.Span(request.Status);
                        text.Span("    Current Stage: ").SemiBold();
                        text.Span(request.CurrentStage.ToString());
                        text.Span("    Revision: ").SemiBold();
                        text.Span(request.RevisionNo.ToString());
                    });
                });

                page.Footer()
                    .PaddingTop(8)
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
            });
        }).GeneratePdf(outputPath);
    }

    public async Task<string> GeneratePreviewPdfAsync(int requestId, CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var info = await db.DCRRequests.AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => new { x.DCRNumber, x.RevisionNo })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DCRManagementSystem",
            "PdfPreview");
        Directory.CreateDirectory(root);
        CleanupOldPreviewFiles(root);
        var outputPath = Path.Combine(
            root,
            $"{SanitizeFileName(info.DCRNumber)}_R{info.RevisionNo}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.pdf");

        await ExportAsync(requestId, outputPath);
        return outputPath;
    }

    public async Task<PdfMailAttachment> GenerateWorkerMailAttachmentPdfAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var info = await db.DCRRequests.AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => new { x.DCRNumber, x.RevisionNo })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy DCR để tạo PDF đính kèm email.");

        // The central worker only needs a durable local file for its send/retry cycle.
        // This fallback deliberately does not depend on the shared file-storage path.
        var localPath = await GeneratePreviewPdfAsync(requestId, cancellationToken);
        var fileInfo = new FileInfo(localPath);
        return new PdfMailAttachment(
            localPath,
            $"{SanitizeFileName(info.DCRNumber)}_R{info.RevisionNo}.pdf",
            "application/pdf",
            string.Empty,
            fileInfo.Length);
    }

    public async Task<PdfMailAttachment> GenerateMailAttachmentPdfAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var info = await db.DCRRequests.AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => new { x.DCRNumber, x.RevisionNo, x.CurrentStage })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");

        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DCRManagementSystem",
            "MailPdfStaging");
        Directory.CreateDirectory(localRoot);
        var attachmentFileName = $"{SanitizeFileName(info.DCRNumber)}_R{info.RevisionNo}.pdf";
        var storedFileName = $"{SanitizeFileName(info.DCRNumber)}_R{info.RevisionNo}_S{info.CurrentStage}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.pdf";
        var localPath = Path.Combine(localRoot, $"{Guid.NewGuid():N}.pdf");

        try
        {
            await ExportAsync(requestId, localPath);

            var storageConfig = await _storage.GetAsync();
            var storageRoot = _storage.ResolveRoot(storageConfig);
            var targetFolder = Path.Combine(
                storageRoot,
                SanitizeFileName(info.DCRNumber),
                $"R{info.RevisionNo}",
                "GeneratedPdf");
            await NetworkResilienceService.ExecuteFileAsync(
                token => Task.Run(() => Directory.CreateDirectory(targetFolder), token),
                cancellationToken);

            var networkPath = Path.Combine(targetFolder, storedFileName);
            var transfer = await OptimizedFileTransferService.CopyWithSha256Async(localPath, networkPath, cancellationToken);
            return new PdfMailAttachment(networkPath, attachmentFileName, "application/pdf", transfer.Sha256, transfer.Length);
        }
        finally
        {
            TryDelete(localPath);
        }
    }

    public async Task<string> GenerateFinalApprovedPdfAsync(int requestId, int? actorUserId = null)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await db.DCRRequests.SingleOrDefaultAsync(x => x.Id == requestId)
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");

        if (request.Status != RequestStatuses.Approved)
        {
            throw new InvalidOperationException("Chỉ DCR Approved mới được sinh Final PDF.");
        }

        var signedRows = await db.DCRApprovalFlows
            .AsNoTracking()
            .Where(x => x.RequestId == requestId && x.SignatureHash != string.Empty)
            .ToListAsync();
        var verifier = new ApprovalSignatureService(_settings);
        var invalidSignature = signedRows.FirstOrDefault(x =>
            !x.ApproverId.HasValue ||
            !x.AuthenticatedAt.HasValue ||
            !verifier.Verify(
                x.RequestId,
                x.RevisionNo,
                x.StageNumber,
                x.ApproverId!.Value,
                x.Decision,
                x.Comments,
                x.AuthenticatedAt!.Value,
                x.WindowsIdentity,
                x.SignatureHash));
        if (invalidSignature is not null)
            throw new InvalidDataException($"Approval signature không hợp lệ tại revision {invalidSignature.RevisionNo}, stage {invalidSignature.StageNumber}.");

        var root = _settings.GetAbsoluteApprovedPdfRoot();
        Directory.CreateDirectory(root);
        var outputPath = Path.Combine(root, $"{request.DCRNumber}_R{request.RevisionNo}_APPROVED.pdf");

        try
        {
            await ExportAsync(requestId, outputPath);

            await using var stream = File.OpenRead(outputPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

            request.FinalPdfPath = Path.GetRelativePath(root, outputPath);
            request.FinalPdfSha256 = hash;
            request.FinalPdfGeneratedAt = DateTime.Now;

            db.AuditLogs.Add(new AuditLog
            {
                RequestId = request.Id,
                UserId = actorUserId ?? request.CreatedBy,
                Action = "Final PDF Generated",
                EntityName = "DCRRequest",
                EntityId = request.Id.ToString(),
                OldValue = string.Empty,
                NewValue = $"{request.FinalPdfPath}; SHA256={hash}",
                ComputerName = AuditEnvironment.ComputerName,
                IpAddress = AuditEnvironment.LocalIpAddress,
                WindowsIdentity = AuditEnvironment.WindowsIdentityName,
                SessionId = RequestExecutionContext.SessionId,
                CreatedAt = DateTime.Now
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return outputPath;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    public async Task<string> GetVerifiedFinalApprovedPdfPathAsync(int requestId)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var data = await db.DCRRequests
            .AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => new { x.Status, x.FinalPdfPath, x.FinalPdfSha256 })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");

        if (data.Status != RequestStatuses.Approved || string.IsNullOrWhiteSpace(data.FinalPdfPath))
            throw new InvalidOperationException("DCR chưa có Final Approved PDF.");

        var path = Path.Combine(_settings.GetAbsoluteApprovedPdfRoot(), data.FinalPdfPath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Final Approved PDF không còn tồn tại trên storage.", path);

        if (!string.IsNullOrWhiteSpace(data.FinalPdfSha256))
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (!actual.Equals(data.FinalPdfSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Final Approved PDF không vượt qua kiểm tra SHA-256.");
        }

        return path;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void CleanupOldPreviewFiles(string root)
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-2);
            foreach (var file in Directory.EnumerateFiles(root, "*.pdf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary preview/staging cleanup is best effort.
        }
    }

    private static IContainer SectionTitle(IContainer container)
    {
        return container
            .PaddingTop(4)
            .PaddingBottom(3)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten1);
    }

    private static IContainer Cell(IContainer container)
    {
        return container
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(4);
    }

    private static IContainer LabelCell(IContainer container)
    {
        return Cell(container)
            .Background(Colors.Grey.Lighten4);
    }

    private static void HeaderCell(TableDescriptor table, string text)
    {
        table.Cell()
            .Element(Cell)
            .Background(Colors.Grey.Lighten3)
            .Text(text)
            .SemiBold();
    }

    private static void BodyCell(TableDescriptor table, string? text)
    {
        table.Cell().Element(Cell).Text(text ?? string.Empty);
    }

    private static void LabelValueRow(
        TableDescriptor table,
        string label1,
        string value1,
        string label2,
        string value2)
    {
        table.Cell().Element(LabelCell).Text(label1).SemiBold();
        table.Cell().Element(Cell).Text(value1 ?? string.Empty);
        table.Cell().Element(LabelCell).Text(label2).SemiBold();
        table.Cell().Element(Cell).Text(value2 ?? string.Empty);
    }

    private static void TwoColumnRow(
        TableDescriptor table,
        string label,
        string? value)
    {
        table.Cell().Element(LabelCell).Text(label).SemiBold();
        table.Cell().Element(Cell).Text(value ?? string.Empty);
    }

    private static string YesNo(bool value) => value ? "Y" : "N";

    private static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";

    private static string FormatDateTime(DateTime? value) =>
        value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm:ss") : "-";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:N1} KB";
        }

        return $"{bytes / 1024d / 1024d:N1} MB";
    }
}
