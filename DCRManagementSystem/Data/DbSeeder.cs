using DCRManagementSystem.Models;
using DCRManagementSystem.Services;

namespace DCRManagementSystem.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Automatic seed/Ensure calls are intentionally disabled. A new database keeps
        // empty data tables so administrators can establish all master data themselves.
        // EnsureRoles(db);
        // EnsureMasterData(db);
        // EnsureBusinessUnits(db);
        // EnsureDepartments(db);
        // EnsureUsers(db);
        // EnsureOrganizationalHierarchy(db);
        // CreateInitialUserAssignments(db);
        // EnsureDepartmentApprovalOwners(db);
        // EnsureWorkflow(db);
        // EnsureApprovalMatrix(db);
    }

    private static void EnsureRoles(AppDbContext db)
    {
        var defaults = new[]
        {
            new RoleDefinition
            {
                RoleName = RoleNames.Administrator,
                Description = "System administration and unrestricted configuration access.",
                HierarchyLevel = 100,
                IsSystemProtected = true,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.CEO,
                Description = "Chief Executive Officer.",
                HierarchyLevel = 90,
                IsSystemProtected = false,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.DCEO,
                Description = "Deputy Chief Executive Officer.",
                HierarchyLevel = 80,
                IsSystemProtected = false,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.COO,
                Description = "Chief Operating Officer.",
                HierarchyLevel = 70,
                IsSystemProtected = false,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.CTO,
                Description = "Chief Technology Officer.",
                HierarchyLevel = 70,
                IsSystemProtected = false,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.Director,
                Description = "Block / functional Director. Directors belong to a Business Unit, not a Department.",
                HierarchyLevel = 30,
                IsSystemProtected = false,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.Manager,
                Description = "Direct Manager. Suggested as first approval level for Staff in the same department.",
                HierarchyLevel = 20,
                IsSystemProtected = false,
                IsActive = true
            },
            new RoleDefinition
            {
                RoleName = RoleNames.Staff,
                Description = "Standard user / initiator role.",
                HierarchyLevel = 10,
                IsSystemProtected = false,
                IsActive = true
            }
        };

        var existing = db.Roles.Select(x => x.RoleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var role in defaults.Where(x => !existing.Contains(x.RoleName)))
            db.Roles.Add(role);

        db.SaveChanges();

        // Normalize roles created by older versions into the current organization model.
        foreach (var user in db.Users)
        {
            var normalized = user.Role switch
            {
                "Administrator" => RoleNames.Administrator,
                "ChiefEngineer" => RoleNames.Director,
                "DesignManager" => RoleNames.Manager,
                "MEManager" => RoleNames.Manager,
                "Initiator" => RoleNames.Staff,
                "Viewer" => RoleNames.Staff,
                _ => user.Role
            };

            if (!string.Equals(user.Role, normalized, StringComparison.Ordinal))
                user.Role = normalized;
        }

        foreach (var workflow in db.WorkflowStageTemplates)
        {
            workflow.ApproverRole = workflow.ApproverRole switch
            {
                "ChiefEngineer" => RoleNames.Director,
                "DesignManager" => RoleNames.Manager,
                "MEManager" => RoleNames.Manager,
                "Initiator" => RoleNames.Staff,
                "Viewer" => RoleNames.Staff,
                _ => workflow.ApproverRole
            };
        }

        foreach (var rule in db.ApprovalMatrixRules)
        {
            rule.ApproverRole = rule.ApproverRole switch
            {
                "ChiefEngineer" => RoleNames.Director,
                "DesignManager" => RoleNames.Manager,
                "MEManager" => RoleNames.Manager,
                "Initiator" => RoleNames.Staff,
                "Viewer" => RoleNames.Staff,
                _ => rule.ApproverRole
            };
        }

        db.SaveChanges();
    }

    private static void EnsureMasterData(AppDbContext db)
    {
        const string seedMarker = "MasterDataDefaultsSeededV1";
        if (db.SystemSettings.Any(x => x.Key == seedMarker))
            return;

        var productLines = new[]
        {
            (Name: "S16", SortOrder: 10),
            (Name: "S16Pre", SortOrder: 20),
            (Name: "S16New", SortOrder: 30)
        };
        var existingProductLines = db.ProductLines.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in productLines.Where(x => !existingProductLines.Contains(x.Name)))
        {
            db.ProductLines.Add(new ProductLineDefinition
            {
                Name = item.Name,
                SortOrder = item.SortOrder,
                IsActive = true
            });
        }

        var changeTypes = new[]
        {
            (Name: "Material", SortOrder: 10),
            (Name: "Machine", SortOrder: 20),
            (Name: "Man", SortOrder: 30),
            (Name: "Method", SortOrder: 40),
            (Name: "Other", SortOrder: 50)
        };
        var existingChangeTypes = db.PartChangeTypes.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in changeTypes.Where(x => !existingChangeTypes.Contains(x.Name)))
        {
            db.PartChangeTypes.Add(new PartChangeTypeDefinition
            {
                Name = item.Name,
                SortOrder = item.SortOrder,
                IsActive = true
            });
        }

        db.SystemSettings.Add(new SystemSetting
        {
            Key = seedMarker,
            Value = "1",
            UpdatedAt = DateTime.Now
        });
        db.SaveChanges();
    }

    private static void EnsureBusinessUnits(AppDbContext db)
    {
        if (!db.BusinessUnits.Any(x => x.UnitCode == "PROD"))
        {
            db.BusinessUnits.Add(new BusinessUnit
            {
                UnitCode = "PROD",
                UnitName = "Khối Sản xuất",
                IsActive = true
            });
            db.SaveChanges();
        }
    }

    private static void EnsureDepartments(AppDbContext db)
    {
        var defaults = new[]
        {
            (Code: "DC", Name: "Design Center"),
            (Code: "GA", Name: "GA Shop Automotive"),
            (Code: "ME", Name: "Manufacturing Engineering"),
            (Code: "PE", Name: "Product Engineering"),
            (Code: "QA", Name: "Quality Assurance")
        };

        var productionUnit = db.BusinessUnits.Single(x => x.UnitCode == "PROD");
        var existingCodes = db.Departments
            .Select(x => x.DepartmentCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in defaults.Where(x => !existingCodes.Contains(x.Code)))
        {
            db.Departments.Add(new Department
            {
                DepartmentCode = item.Code,
                DepartmentName = item.Name,
                BusinessUnitId = productionUnit.Id,
                IsActive = true
            });
        }

        db.SaveChanges();

        foreach (var department in db.Departments.Where(x => !x.BusinessUnitId.HasValue))
            department.BusinessUnitId = productionUnit.Id;
        db.SaveChanges();
    }

    private static void EnsureUsers(AppDbContext db)
    {
        if (db.Users.Any())
            return;

        var dc = db.Departments.Single(x => x.DepartmentCode == "DC");
        var ga = db.Departments.Single(x => x.DepartmentCode == "GA");
        var me = db.Departments.Single(x => x.DepartmentCode == "ME");
        var pe = db.Departments.Single(x => x.DepartmentCode == "PE");
        var qa = db.Departments.Single(x => x.DepartmentCode == "QA");
        var productionUnit = db.BusinessUnits.Single(x => x.UnitCode == "PROD");

        db.Users.AddRange(
            new User
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash("Admin@123"),
                FullName = "System Administrator",
                Email = "admin@local",
                Role = RoleNames.Administrator,
                DepartmentId = dc.Id,
                BusinessUnitId = productionUnit.Id
            },
            new User
            {
                Username = "initiator",
                PasswordHash = PasswordHasher.Hash("Demo@123"),
                FullName = "DCR Staff",
                Email = "initiator@local",
                Role = RoleNames.Staff,
                DepartmentId = dc.Id,
                BusinessUnitId = productionUnit.Id
            },
            new User
            {
                Username = "design.manager",
                PasswordHash = PasswordHasher.Hash("Demo@123"),
                FullName = "Design Manager",
                Email = "design.manager@local",
                Role = RoleNames.Manager,
                DepartmentId = dc.Id,
                BusinessUnitId = productionUnit.Id
            },
            new User
            {
                Username = "ga.manager",
                PasswordHash = PasswordHasher.Hash("Demo@123"),
                FullName = "GA Shop Manager",
                Email = "ga.manager@local",
                Role = RoleNames.Manager,
                DepartmentId = ga.Id,
                BusinessUnitId = productionUnit.Id
            },
            new User
            {
                Username = "me.manager",
                PasswordHash = PasswordHasher.Hash("Demo@123"),
                FullName = "ME Manager",
                Email = "me.manager@local",
                Role = RoleNames.Manager,
                DepartmentId = me.Id,
                BusinessUnitId = productionUnit.Id
            },
            new User
            {
                Username = "chief.engineer",
                PasswordHash = PasswordHasher.Hash("Demo@123"),
                FullName = "Engineering Director",
                Email = "chief.engineer@local",
                Role = RoleNames.Director,
                DepartmentId = null,
                BusinessUnitId = productionUnit.Id
            },
            new User
            {
                Username = "qa.manager",
                PasswordHash = PasswordHasher.Hash("Demo@123"),
                FullName = "Quality Manager",
                Email = "qa.manager@local",
                Role = RoleNames.Manager,
                DepartmentId = qa.Id,
                BusinessUnitId = productionUnit.Id
            });

        db.SaveChanges();
    }

    private static void EnsureOrganizationalHierarchy(AppDbContext db)
    {
        var fallbackUnit = db.BusinessUnits.OrderBy(x => x.Id).First();
        var departments = db.Departments.ToList();
        foreach (var department in departments.Where(x => !x.BusinessUnitId.HasValue))
            department.BusinessUnitId = fallbackUnit.Id;
        db.SaveChanges();

        var roleLevels = db.Roles.ToDictionary(x => x.RoleName, x => x.HierarchyLevel, StringComparer.OrdinalIgnoreCase);
        var directorLevel = roleLevels.GetValueOrDefault(RoleNames.Director, 30);
        var users = db.Users.Where(x => !x.IsDeleted).ToList();
        foreach (var user in users)
        {
            var level = roleLevels.GetValueOrDefault(user.Role, 0);
            if (level >= directorLevel && !string.Equals(user.Role, RoleNames.Administrator, StringComparison.OrdinalIgnoreCase))
            {
                // Director/CTO/COO/DCEO/CEO and equivalent roles are scoped to a Business Unit,
                // never to an individual Department.
                if (!user.BusinessUnitId.HasValue && user.DepartmentId.HasValue)
                    user.BusinessUnitId = departments.FirstOrDefault(x => x.Id == user.DepartmentId.Value)?.BusinessUnitId;
                user.DepartmentId = null;
            }
            else if (user.DepartmentId.HasValue)
            {
                // Staff/Manager inherit the Business Unit from their Department.
                user.BusinessUnitId = departments.FirstOrDefault(x => x.Id == user.DepartmentId.Value)?.BusinessUnitId;
            }
        }
        db.SaveChanges();

        // Normalize the reporting chain for every configured Business Unit, not only demo data.
        // Staff -> Department Manager -> Business Unit Director -> explicitly configured executives.
        var units = db.BusinessUnits.ToList();
        var activeUsers = db.Users.Where(x => !x.IsDeleted).ToList();
        foreach (var unit in units)
        {
            var director = unit.DirectorUserId.HasValue
                ? activeUsers.FirstOrDefault(x => x.UserId == unit.DirectorUserId.Value)
                : null;
            if (director is null)
            {
                // Safe migration fallback: if exactly/at least one active Director is already scoped to the block,
                // use the first one as block head. Administrator can change it later in Quản trị -> Khối.
                director = activeUsers
                    .Where(x => x.IsActive && x.BusinessUnitId == unit.Id && string.Equals(x.Role, RoleNames.Director, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.UserId)
                    .FirstOrDefault();
                if (director is not null)
                    unit.DirectorUserId = director.UserId;
            }
            if (director is not null)
            {
                director.BusinessUnitId = unit.Id;
                director.DepartmentId = null;
            }

            foreach (var department in departments.Where(x => x.BusinessUnitId == unit.Id))
            {
                var manager = department.ManagerUserId.HasValue
                    ? activeUsers.FirstOrDefault(x => x.UserId == department.ManagerUserId.Value)
                    : null;
                if (manager is not null)
                {
                    manager.DepartmentId = department.Id;
                    manager.BusinessUnitId = unit.Id;
                    manager.DirectManagerUserId = director?.UserId;
                }

                foreach (var staff in activeUsers.Where(x => x.DepartmentId == department.Id && string.Equals(x.Role, RoleNames.Staff, StringComparison.OrdinalIgnoreCase)))
                {
                    staff.BusinessUnitId = unit.Id;
                    staff.DirectManagerUserId = manager?.UserId;
                }
            }
        }
        db.SaveChanges();
    }

    private static void EnsureDepartmentApprovalOwners(AppDbContext db)
    {
        var managerUsernames = new Dictionary<string, string>
        {
            ["DC"] = "design.manager",
            ["GA"] = "ga.manager",
            ["ME"] = "me.manager",
            ["QA"] = "qa.manager"
        };

        foreach (var pair in managerUsernames)
        {
            var department = db.Departments.SingleOrDefault(x => x.DepartmentCode == pair.Key);
            var user = db.Users.SingleOrDefault(x => x.Username == pair.Value && !x.IsDeleted);
            if (department is not null && user is not null && !department.ManagerUserId.HasValue)
                department.ManagerUserId = user.UserId;
        }

        // Director belongs to the Business Unit, not to an individual Department.
        var productionUnit = db.BusinessUnits.SingleOrDefault(x => x.UnitCode == "PROD");
        var director = db.Users.SingleOrDefault(x => x.Username == "chief.engineer" && !x.IsDeleted);
        if (productionUnit is not null && director is not null)
        {
            director.BusinessUnitId = productionUnit.Id;
            director.DepartmentId = null;
            if (!productionUnit.DirectorUserId.HasValue)
                productionUnit.DirectorUserId = director.UserId;
        }

        foreach (var department in db.Departments.Where(x => x.ManagerUserId.HasValue).ToList())
        {
            var manager = db.Users.SingleOrDefault(x => x.UserId == department.ManagerUserId.Value && !x.IsDeleted);
            if (manager is null) continue;
            manager.DepartmentId = department.Id;
            manager.BusinessUnitId = department.BusinessUnitId;
            if (productionUnit is not null && department.BusinessUnitId == productionUnit.Id && director is not null && manager.UserId != director.UserId)
                manager.DirectManagerUserId ??= director.UserId;
        }

        var initiator = db.Users.SingleOrDefault(x => x.Username == "initiator" && !x.IsDeleted);
        var designManager = db.Users.SingleOrDefault(x => x.Username == "design.manager" && !x.IsDeleted);
        if (initiator is not null && designManager is not null && !initiator.DirectManagerUserId.HasValue)
            initiator.DirectManagerUserId = designManager.UserId;

        db.SaveChanges();
    }

    private static void CreateInitialUserAssignments(AppDbContext db)
    {
        foreach (var user in db.Users.Where(x => !x.IsDeleted).ToList())
        {
            if (user.BusinessUnitId.HasValue && !db.UserBusinessUnitAssignments.Any(x =>
                    x.UserId == user.UserId && x.BusinessUnitId == user.BusinessUnitId.Value))
            {
                db.UserBusinessUnitAssignments.Add(new UserBusinessUnitAssignment
                {
                    UserId = user.UserId,
                    BusinessUnitId = user.BusinessUnitId.Value,
                    IsPrimary = true,
                    JobTitle = user.Role,
                    ReportsToUserId = user.DirectManagerUserId
                });
            }

            if (user.DepartmentId.HasValue && !db.UserDepartmentAssignments.Any(x =>
                    x.UserId == user.UserId && x.DepartmentId == user.DepartmentId.Value))
            {
                db.UserDepartmentAssignments.Add(new UserDepartmentAssignment
                {
                    UserId = user.UserId,
                    DepartmentId = user.DepartmentId.Value,
                    IsPrimary = true,
                    JobTitle = user.Role,
                    ReportsToUserId = user.DirectManagerUserId
                });
            }
        }
        db.SaveChanges();
    }

    private static void EnsureWorkflow(AppDbContext db)
    {
        if (db.WorkflowStageTemplates.Any())
            return;

        db.WorkflowStageTemplates.AddRange(
            new WorkflowStageTemplate
            {
                StageNumber = 1,
                StageCode = WorkflowStageCodes.Submission,
                StageName = "Request Submission",
                ApproverRole = RoleNames.Staff,
                IsActive = true
            },
            new WorkflowStageTemplate
            {
                StageNumber = 2,
                StageCode = WorkflowStageCodes.DesignManager,
                StageName = "Direct Manager Approval",
                ApproverRole = RoleNames.Manager,
                IsActive = true
            },
            new WorkflowStageTemplate
            {
                StageNumber = 3,
                StageCode = WorkflowStageCodes.ImpactedDepartment,
                StageName = "Impacted department Approval",
                ApproverRole = string.Empty,
                IsImpactedDepartmentStage = true,
                IsActive = true
            },
            new WorkflowStageTemplate
            {
                StageNumber = 4,
                StageCode = WorkflowStageCodes.MEManager,
                StageName = "Manager Approval",
                ApproverRole = RoleNames.Manager,
                IsActive = true
            },
            new WorkflowStageTemplate
            {
                StageNumber = 5,
                StageCode = WorkflowStageCodes.ChiefEngineer,
                StageName = "Director Approval",
                ApproverRole = RoleNames.Director,
                IsActive = true
            });

        db.SaveChanges();
    }

    private static void EnsureApprovalMatrix(AppDbContext db)
    {
        if (db.ApprovalMatrixRules.Any())
            return;

        var me = db.Departments.SingleOrDefault(x => x.DepartmentCode == "ME");
        var pe = db.Departments.SingleOrDefault(x => x.DepartmentCode == "PE");

        db.ApprovalMatrixRules.Add(new ApprovalMatrixRule
        {
            StageCode = WorkflowStageCodes.DesignManager,
            ApproverSource = ApproverSources.RequestingDepartmentManager,
            Priority = 10,
            IsActive = true,
            Description = "Route to direct Manager of Requesting Department"
        });

        db.ApprovalMatrixRules.Add(new ApprovalMatrixRule
        {
            StageCode = WorkflowStageCodes.ImpactedDepartment,
            ApproverSource = ApproverSources.ImpactedDepartmentManager,
            Priority = 10,
            IsActive = true,
            Description = "Parallel route to Managers of all impacted departments"
        });

        if (me is not null)
        {
            db.ApprovalMatrixRules.Add(new ApprovalMatrixRule
            {
                StageCode = WorkflowStageCodes.MEManager,
                ApproverSource = ApproverSources.TargetDepartmentManager,
                TargetDepartmentId = me.Id,
                Priority = 10,
                IsActive = true,
                Description = "Route to ME Department Manager"
            });
        }

        if (pe is not null)
        {
            db.ApprovalMatrixRules.Add(new ApprovalMatrixRule
            {
                StageCode = WorkflowStageCodes.ChiefEngineer,
                ApproverSource = ApproverSources.Role,
                ApproverRole = RoleNames.Director,
                TargetDepartmentId = pe.Id,
                Priority = 10,
                IsActive = true,
                Description = "Route to Product Engineering Director"
            });
        }

        db.SaveChanges();
    }
}
