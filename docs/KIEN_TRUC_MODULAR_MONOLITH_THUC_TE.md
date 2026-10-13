# Kiến trúc Modular Monolith thực tế - Tutor Management System

**Phiên bản:** 2.0  
**Ngày:** 13/10/2026  
**Trạng thái:** Kiến trúc khuyến nghị chính thức  
**Stack:** .NET 10, ASP.NET Core, PostgreSQL, EF Core, React/TypeScript

---

## 1. Mục tiêu kiến trúc

Hai tài liệu LAB chỉ được dùng để giữ các chuẩn thiết kế API tốt:

- Controller không chứa business logic.
- Entity không được trả trực tiếp cho client.
- Request/Response model tách biệt.
- REST resource routes, plural nouns, route constraints và API versioning.
- Search, sort, paging, field selection và expansion.
- Response format nhất quán.
- Content negotiation JSON/XML và HTTP 406.
- Validation ở nhiều lớp.
- Global exception handling và request logging.
- JWT, refresh token, role/policy authorization.
- Swagger/OpenAPI và Docker.

Cấu trúc project/folder không bị giới hạn bởi ba project của LAB. Hệ thống dùng:

> **Modular Monolith + Domain-Driven Design theo bounded context + Clean Architecture bên trong từng module + Vertical Slice theo use case.**

Mục tiêu:

1. Một hệ thống deploy đơn giản như monolith.
2. Module có ownership và database schema rõ ràng.
3. Business logic không tập trung vào các lớp `Service` lớn.
4. Có thể thêm hàng trăm use case mà không biến codebase thành “big ball of mud”.
5. Có đường tách module thành service độc lập nếu sau này thật sự cần.

---

## 2. Kiến trúc tổng thể

```text
Browser / React SPA
        │
        ▼
TutorManagement.Api (HTTP Host)
        │
        ├─ Identity & Access module
        ├─ Organization module
        ├─ Academics module
        ├─ Learning Operations module
        ├─ Finance module
        ├─ Human Resources module
        ├─ Compliance module
        ├─ Notifications module
        └─ Documents & Exams module
                 │
                 ▼
        PostgreSQL - one physical database
        each module owns one logical schema

TutorManagement.Worker
  ├─ Outbox dispatcher
  ├─ Notification jobs
  ├─ Report generation
  └─ Document/exam processing

TutorManagement.DbMigrator
  └─ Applies reviewed EF Core migrations before API rollout
```

### 2.1. Một deployment, nhiều module

- API modules được compile cùng solution và chạy trong một API process.
- Worker là process phụ, dùng cùng source/module contracts nhưng scale độc lập.
- PostgreSQL là một database vật lý; mỗi module sở hữu schema và tables của nó.
- Module không đọc trực tiếp table/schema của module khác.
- Giao tiếp liên module qua public application contract hoặc integration event.

### 2.2. Khi nào đây vẫn là monolith

Đây vẫn là monolith vì:

- Một release train.
- Một API deployment chính.
- Một database PostgreSQL.
- Không có network call nội bộ giữa các module.
- Có thể transaction cục bộ và debug toàn hệ thống trong một solution.

Worker/DbMigrator là host vận hành cùng hệ thống, không biến kiến trúc thành microservices.

---

## 3. Bounded contexts

Không tạo một module cho mỗi menu. Nhóm chức năng theo ngôn ngữ và transaction boundary.

| Module | Trách nhiệm sở hữu | Không sở hữu |
|---|---|---|
| IdentityAccess | User, credential, role, permission, session, refresh token | Hồ sơ giáo viên/học sinh |
| Organizations | Tenant, cơ sở, phòng học, legal profile, subscription capability | Lịch học, tiền |
| Academics | Học sinh, người giám hộ, giáo viên học thuật, môn, lớp, enrollment | Session thực tế, payment |
| LearningOperations | Thời khóa biểu, session, phòng đặt lịch, điểm danh, học bù | Công nợ và payroll ledger |
| Finance | Fee plan, charge, payment, allocation, receipt, expense, reconciliation | Hồ sơ lớp chi tiết |
| HumanResources | Nhân sự, hợp đồng, pay scheme, payroll, payslip | Authentication credential |
| Compliance | Disclosure, consent evidence, legal checklist, tax rule/draft | Thu tiền thực tế |
| Notifications | Template, preference, delivery, provider adapter | Nghiệp vụ tạo thông báo |
| DocumentsExams | File, retention, import/export, exam processing job | Core academic state |
| Reporting | Read models, dashboard, export projection | Quyền sửa source transactions |

### 3.1. Quy tắc ownership

- Chỉ module owner được ghi dữ liệu của module.
- Module khác không import entity hoặc repository implementation của owner.
- ID được truyền qua boundary dưới dạng scalar/strongly typed ID.
- Dữ liệu cần đọc thường xuyên được cung cấp qua contract hoặc replicated read model.
- Reporting được phép đọc read replica/materialized views đã công bố, không trở thành nơi sửa dữ liệu.

### 3.2. Transaction boundary

Ví dụ:

- `LearningOperations` hoàn tất buổi học trong transaction của nó.
- Nó ghi `SessionCompletedIntegrationEvent` vào outbox cùng transaction.
- `Finance` nhận event và tạo charge idempotently.
- `HumanResources` nhận event và tạo earning item idempotently.

Không dùng distributed transaction. Eventual consistency phải được hiển thị rõ trong UI khi cần.

---

## 4. Solution structure

```text
TutorManagementSystem/
├─ TutorManagementSystem.sln
├─ TutorManagementSystem.Api.slnf
├─ TutorManagementSystem.Tests.slnf
├─ Directory.Build.props
├─ Directory.Build.targets
├─ Directory.Packages.props
├─ global.json
├─ .editorconfig
├─ .gitignore
├─ .env.example
├─ docker-compose.yml
├─ README.md
│
├─ src/
│  ├─ Hosts/
│  │  ├─ TutorManagement.Api/
│  │  │  ├─ Composition/
│  │  │  │  ├─ ModuleRegistration.cs
│  │  │  │  ├─ AuthenticationRegistration.cs
│  │  │  │  └─ OpenApiRegistration.cs
│  │  │  ├─ Middleware/
│  │  │  │  ├─ ExceptionHandlingMiddleware.cs
│  │  │  │  ├─ RequestLoggingMiddleware.cs
│  │  │  │  └─ CorrelationIdMiddleware.cs
│  │  │  ├─ HealthChecks/
│  │  │  ├─ appsettings.json
│  │  │  ├─ appsettings.Development.json
│  │  │  ├─ Dockerfile
│  │  │  └─ Program.cs
│  │  ├─ TutorManagement.Worker/
│  │  │  ├─ Jobs/
│  │  │  ├─ Consumers/
│  │  │  ├─ Dockerfile
│  │  │  └─ Program.cs
│  │  └─ TutorManagement.DbMigrator/
│  │     ├─ MigrationRunner.cs
│  │     ├─ Dockerfile
│  │     └─ Program.cs
│  │
│  ├─ BuildingBlocks/
│  │  ├─ TutorManagement.SharedKernel/
│  │  │  ├─ Domain/
│  │  │  │  ├─ Entity.cs
│  │  │  │  ├─ AggregateRoot.cs
│  │  │  │  ├─ ValueObject.cs
│  │  │  │  ├─ DomainEvent.cs
│  │  │  │  └─ DomainException.cs
│  │  │  └─ Primitives/
│  │  │     ├─ Result.cs
│  │  │     ├─ Error.cs
│  │  │     └─ StronglyTypedId.cs
│  │  ├─ TutorManagement.Application.Abstractions/
│  │  │  ├─ Messaging/
│  │  │  │  ├─ ICommand.cs
│  │  │  │  ├─ ICommandHandler.cs
│  │  │  │  ├─ IQuery.cs
│  │  │  │  └─ IQueryHandler.cs
│  │  │  ├─ Behaviors/
│  │  │  ├─ Clock/
│  │  │  ├─ Security/
│  │  │  └─ Transactions/
│  │  ├─ TutorManagement.Infrastructure/
│  │  │  ├─ Persistence/
│  │  │  ├─ Outbox/
│  │  │  ├─ Caching/
│  │  │  ├─ Files/
│  │  │  └─ Observability/
│  │  └─ TutorManagement.WebApi/
│  │     ├─ Responses/
│  │     │  ├─ ApiResponse.cs
│  │     │  ├─ PagedResponse.cs
│  │     │  ├─ ApiError.cs
│  │     │  └─ PaginationMetadata.cs
│  │     ├─ Versioning/
│  │     ├─ ModelBinding/
│  │     └─ OpenApi/
│  │
│  └─ Modules/
│     ├─ IdentityAccess/
│     ├─ Organizations/
│     ├─ Academics/
│     ├─ LearningOperations/
│     ├─ Finance/
│     ├─ HumanResources/
│     ├─ Compliance/
│     ├─ Notifications/
│     ├─ DocumentsExams/
│     └─ Reporting/
│
├─ frontend/
│  ├─ apps/
│  │  ├─ admin-web/
│  │  └─ parent-portal/
│  ├─ packages/
│  │  ├─ api-client/
│  │  ├─ design-system/
│  │  ├─ auth/
│  │  └─ shared-utils/
│  ├─ package.json
│  ├─ pnpm-workspace.yaml
│  └─ vite.config.ts
│
├─ tests/
│  ├─ ArchitectureTests/
│  ├─ ContractTests/
│  ├─ IntegrationTests/
│  ├─ EndToEndTests/
│  └─ PerformanceTests/
│
├─ deploy/
│  ├─ docker/
│  ├─ compose/
│  ├─ kubernetes/
│  └─ terraform/
│
├─ tools/
│  ├─ seed-data/
│  ├─ migration-check/
│  └─ api-client-generation/
│
└─ docs/
   ├─ adr/
   ├─ architecture/
   ├─ api/
   ├─ database/
   ├─ runbooks/
   └─ security/
```

---

## 5. Cấu trúc chuẩn bên trong một module

Mỗi bounded context lớn có bốn project. Ví dụ `Finance`:

```text
Modules/Finance/
├─ TutorManagement.Modules.Finance.Domain/
│  ├─ Charges/
│  │  ├─ Charge.cs
│  │  ├─ ChargeLine.cs
│  │  ├─ ChargeStatus.cs
│  │  ├─ ChargeCreatedDomainEvent.cs
│  │  └─ IChargeRepository.cs
│  ├─ Payments/
│  │  ├─ Payment.cs
│  │  ├─ PaymentAllocation.cs
│  │  ├─ PaymentMethod.cs
│  │  └─ IPaymentRepository.cs
│  ├─ Receipts/
│  ├─ Refunds/
│  ├─ Expenses/
│  ├─ Reconciliation/
│  └─ Common/
│
├─ TutorManagement.Modules.Finance.Application/
│  ├─ Abstractions/
│  │  ├─ IFinanceUnitOfWork.cs
│  │  ├─ IPaymentGateway.cs
│  │  └─ IReceiptNumberGenerator.cs
│  ├─ Charges/
│  │  ├─ CreateMonthlyCharges/
│  │  │  ├─ CreateMonthlyChargesCommand.cs
│  │  │  ├─ CreateMonthlyChargesValidator.cs
│  │  │  ├─ CreateMonthlyChargesHandler.cs
│  │  │  └─ CreateMonthlyChargesResult.cs
│  │  └─ SearchCharges/
│  ├─ Payments/
│  │  ├─ CollectPayment/
│  │  ├─ AllocatePayment/
│  │  └─ GetPaymentById/
│  ├─ Refunds/
│  ├─ Expenses/
│  ├─ EventHandlers/
│  │  └─ SessionCompletedHandler.cs
│  └─ DependencyInjection.cs
│
├─ TutorManagement.Modules.Finance.Infrastructure/
│  ├─ Persistence/
│  │  ├─ FinanceDbContext.cs
│  │  ├─ Configurations/
│  │  ├─ Repositories/
│  │  ├─ Migrations/
│  │  └─ FinanceUnitOfWork.cs
│  ├─ Gateways/
│  ├─ ReceiptGeneration/
│  ├─ Outbox/
│  └─ DependencyInjection.cs
│
├─ TutorManagement.Modules.Finance.Contracts/
│  ├─ Requests/
│  ├─ Responses/
│  ├─ IntegrationEvents/
│  └─ PublicServices/
│
└─ TutorManagement.Modules.Finance.Api/
   ├─ Controllers/
   │  ├─ V1/
   │  │  ├─ ChargesController.cs
   │  │  ├─ PaymentsController.cs
   │  │  └─ ExpensesController.cs
   │  └─ V2/
   ├─ Mapping/
   ├─ Authorization/
   ├─ OpenApi/
   └─ FinanceModule.cs
```

### 5.1. Project references trong module

```text
Finance.Domain
  -> SharedKernel

Finance.Application
  -> Finance.Domain
  -> Finance.Contracts
  -> Application.Abstractions

Finance.Infrastructure
  -> Finance.Domain
  -> Finance.Application
  -> Infrastructure Building Block

Finance.Api
  -> Finance.Application
  -> Finance.Contracts
  -> WebApi Building Block
```

API Host chỉ gọi `FinanceModule.AddFinanceModule(...)` và `MapFinanceEndpoints(...)`.

### 5.2. Module nhỏ

Module nhỏ ban đầu có thể dùng một project với các folder `Domain/Application/Infrastructure/Api`. Khi module lớn hoặc cần compile-time boundary, tách thành bốn project mà không thay đổi namespace công khai.

---

## 6. Chia logic nghiệp vụ

| Logic | Nơi đặt | Ví dụ |
|---|---|---|
| Invariant một aggregate | Domain entity | Payment không thể allocate quá số tiền |
| Giá trị và equality | Value object | Money, Email, DateRange |
| Rule thuần nghiệp vụ nhiều object | Domain policy/service | Tuition calculation policy |
| Điều phối use case | Command handler | CollectPaymentHandler |
| Read use case | Query handler | SearchOutstandingChargesHandler |
| Input syntax/shape | Request + validator | Email, range, required |
| Rule cần kiểm tra DB | Handler/domain policy qua port | Duplicate enrollment |
| SQL/query optimization | Infrastructure query repository | Search, sorting, projection |
| HTTP concern | Controller/endpoint | Status, route, Accept header |
| External integration | Infrastructure adapter | SMS, payment, e-invoice |

### 6.1. Không dùng business service tổng hợp

Không tạo:

```text
FinanceService
  CreateFee()
  UpdateFee()
  DeleteFee()
  CollectPayment()
  Refund()
  ExportTax()
  SendReminder()
  ...
```

Tạo một handler cho một intent:

```text
CreateChargeHandler
CollectPaymentHandler
RefundPaymentHandler
VoidReceiptHandler
ReconcileBankTransactionHandler
```

### 6.2. Domain service chỉ khi thật sự cần

Domain service dùng khi rule:

- Không thuộc tự nhiên về một entity.
- Dùng nhiều domain object.
- Không phụ thuộc database/HTTP/framework.

Nếu chỉ orchestration, dùng application handler thay vì domain service.

### 6.3. Query side

Không ép mọi query qua aggregate repository. Query báo cáo/list phức tạp dùng query service hoặc Dapper trong Infrastructure, trả read model của Application.

Command vẫn đi qua aggregate để bảo vệ invariant.

---

## 7. Giao tiếp liên module

### 7.1. Không tham chiếu nội bộ module khác

Sai:

```text
Finance.Application -> LearningOperations.Infrastructure
Finance handler -> LearningOperationsDbContext
Payroll repository -> attendance.sessions table
```

Đúng:

- Public application interface trong `Contracts/PublicServices` cho synchronous query nhỏ.
- Integration event trong `Contracts/IntegrationEvents` cho side effect.
- Read model được publish cho Reporting.

### 7.2. Integration event

```csharp
public sealed record SessionCompletedIntegrationEvent(
    Guid EventId,
    Guid TenantId,
    Guid SessionId,
    Guid ClassId,
    Guid TeacherId,
    DateTime CompletedAtUtc,
    IReadOnlyCollection<AttendanceSnapshotItem> Attendance);
```

Consumer phải idempotent theo `EventId` và business key.

### 7.3. Outbox/inbox

- Domain change và outbox message ghi trong cùng transaction.
- Worker publish message sau commit.
- Consumer ghi inbox record trước/đồng transaction với side effect.
- Retry không tạo charge/lương/thông báo trùng.
- Failed message vào dead-letter store và có màn hình vận hành.

---

## 8. API standards giữ từ hai LAB

Folder không theo LAB nhưng API behavior vẫn tuân thủ.

### 8.1. Routes

```text
GET    /api/v1/students
POST   /api/v1/students
GET    /api/v1/students/{studentId:guid}
PATCH  /api/v1/students/{studentId:guid}

GET    /api/v1/classes/{classId:guid}/students
POST   /api/v1/classes/{classId:guid}/enrollments
GET    /api/v1/teaching-sessions/{sessionId:guid}/attendance
PUT    /api/v1/teaching-sessions/{sessionId:guid}/attendance

GET    /api/v1/charges
POST   /api/v1/payments
POST   /api/v1/payments/{paymentId:guid}/refunds
```

Quy tắc:

- Resource names dùng plural nouns.
- Không dùng `/getStudents`, `/createPayment`.
- Command không ánh xạ tự nhiên thành CRUD có thể dùng subresource/action noun hợp lý như `/refunds`, `/close`, `/approve`.
- Route constraints rõ ràng.
- Named route cho `CreatedAtRoute`.
- URL versioning `/api/v1` và `/api/v2`.

### 8.2. Response envelope

```json
{
  "success": true,
  "message": "Request processed successfully",
  "data": {},
  "errors": null,
  "traceId": "00-..."
}
```

Paged response:

```json
{
  "success": true,
  "message": "Request processed successfully",
  "data": [],
  "errors": null,
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 120,
    "totalPages": 6
  },
  "traceId": "00-..."
}
```

Error response giữ cùng envelope:

```json
{
  "success": false,
  "message": "Validation failed",
  "data": null,
  "errors": [
    {
      "code": "student.email.invalid",
      "field": "email",
      "message": "Email is invalid"
    }
  ],
  "traceId": "00-..."
}
```

### 8.3. Status codes

- `200`: read/update/command có response.
- `201`: created, có `Location`.
- `204`: delete/command không trả body khi convention chọn.
- `400`: malformed input/query/model binding.
- `401`: chưa xác thực/token sai.
- `403`: không đủ quyền.
- `404`: resource không tồn tại.
- `406`: Accept format không hỗ trợ.
- `409`: duplicate/concurrency/idempotency conflict.
- `422`: input đúng cú pháp nhưng vi phạm business rule, nếu team chọn convention này.
- `500`: lỗi ngoài dự kiến, không lộ chi tiết nội bộ.

### 8.4. Search/sort/paging/fields/expand

```text
GET /api/v1/students
  ?search=nguyen
  &sort=fullName,-dateOfBirth
  &page=2
  &size=20
  &fields=id,fullName,email
  &expand=guardians,enrollments
```

- Tất cả field/sort/expand dùng whitelist.
- Không nối trực tiếp query string thành SQL.
- `size` có giới hạn tối đa.
- Unknown option trả 400.
- Projection thực hiện trước materialization.
- Không expansion graph vô hạn.

### 8.5. Content negotiation

```csharp
services.AddControllers(options =>
{
    options.ReturnHttpNotAcceptable = true;
    options.RespectBrowserAcceptHeader = true;
}).AddXmlSerializerFormatters();
```

- `Accept: application/json` -> JSON.
- `Accept: application/xml` -> XML.
- Unsupported format -> 406.
- Contract models phải serialize được cả hai format.

### 8.6. Validation

Ba lớp:

1. Request validation: Required, length, email, phone, regex.
2. FluentValidation/application validation: cross-field và format phức tạp.
3. Domain validation: invariant không bao giờ được vi phạm.

Không duplicate cùng một rule ở ba nơi nếu không cần.

### 8.7. Exception handling

Global middleware map exception:

| Exception | HTTP |
|---|---:|
| RequestValidationException | 400 |
| AuthenticationException | 401 |
| ForbiddenException | 403 |
| NotFoundException | 404 |
| ConflictException | 409 |
| ConcurrencyException | 409 |
| BusinessRuleException | 422 |
| Unknown | 500 |

Controller không `try/catch` lặp lại.

### 8.8. Request logging

Log method, normalized path, status, duration, correlation ID, tenant ID và user ID. Không log password, token, Authorization header, raw login body hoặc dữ liệu học sinh nhạy cảm.

### 8.9. Authentication/authorization

- Password hash bằng ASP.NET Core PasswordHasher hoặc Argon2id implementation được review.
- JWT secret/key từ secret store/environment.
- Access token ngắn hạn.
- Refresh token chỉ lưu hash, rotate mỗi lần dùng.
- Reuse detection revoke token family.
- Role cho coarse-grained access; permission/policy cho nghiệp vụ.
- Object-level authorization theo tenant/campus/class.

### 8.10. OpenAPI

- Mỗi module đăng ký document/operation filters của nó.
- Swagger có Bearer authentication và Authorize button.
- Schemas, status codes, examples, pagination và version rõ ràng.
- Production có thể tắt Swagger UI công khai nhưng giữ OpenAPI artifact.

---

## 9. PostgreSQL ownership và migrations

### 9.1. Logical schemas

```text
iam.*
organization.*
academics.*
learning.*
finance.*
hr.*
compliance.*
notification.*
document.*
reporting.*
```

Mỗi module có DbContext riêng:

```text
IdentityDbContext
OrganizationDbContext
AcademicsDbContext
LearningOperationsDbContext
FinanceDbContext
HumanResourcesDbContext
ComplianceDbContext
NotificationDbContext
DocumentsDbContext
ReportingDbContext
```

### 9.2. Migration ownership

Migration nằm trong `Infrastructure/Persistence/Migrations` của chính module.

```csharp
options.UseNpgsql(connectionString, npgsql =>
{
    npgsql.MigrationsHistoryTable(
        "__ef_migrations_history",
        "finance");
});
```

Mỗi schema có history table riêng để tránh collision.

### 9.3. DbMigrator host

`TutorManagement.DbMigrator`:

1. Lấy distributed/advisory lock PostgreSQL.
2. Kiểm tra pending migrations từng module.
3. Apply theo thứ tự xác định.
4. Ghi migration audit.
5. Thoát với non-zero code nếu lỗi.

API không gọi `Database.Migrate()` ở production.

### 9.4. Migration commands

Ví dụ Finance:

```powershell
dotnet ef migrations add AddPaymentAllocation `
  --project src/Modules/Finance/TutorManagement.Modules.Finance.Infrastructure `
  --startup-project src/Hosts/TutorManagement.DbMigrator `
  --context FinanceDbContext `
  --output-dir Persistence/Migrations
```

```powershell
dotnet ef migrations script --idempotent `
  --project src/Modules/Finance/TutorManagement.Modules.Finance.Infrastructure `
  --startup-project src/Hosts/TutorManagement.DbMigrator `
  --context FinanceDbContext `
  --output artifacts/migrations/finance.sql
```

### 9.5. Migration rules

- Không sửa migration đã chạy/shared.
- Không đổi schema phá vỡ cùng release với code phụ thuộc.
- Dùng expand -> backfill -> switch -> contract.
- Index lớn tạo theo chiến lược tránh lock phù hợp PostgreSQL.
- Data migration lớn chạy resumable job, không nhét hết vào transaction migration.
- CI dựng database rỗng từ toàn bộ migrations và chạy smoke tests.
- CI kiểm tra model snapshot/pending model changes.
- Backup và restore rehearsal trước thay đổi rủi ro cao.

### 9.6. Cross-schema foreign key

Hạn chế FK xuyên module vì làm module coupling ở database. Module lưu external ID và đảm bảo consistency qua application contract/event. Chỉ dùng cross-schema FK khi đã ghi ADR và chấp nhận coupling lâu dài.

---

## 10. Host và module composition

Mỗi module expose một entry point duy nhất:

```csharp
public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFinanceApplication();
        services.AddFinanceInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapFinanceModule(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllers();
        return endpoints;
    }
}
```

Host `Program.cs` chỉ composition:

```csharp
builder.Services
    .AddPlatformWebApi(builder.Configuration)
    .AddIdentityAccessModule(builder.Configuration)
    .AddOrganizationsModule(builder.Configuration)
    .AddAcademicsModule(builder.Configuration)
    .AddLearningOperationsModule(builder.Configuration)
    .AddFinanceModule(builder.Configuration)
    .AddHumanResourcesModule(builder.Configuration)
    .AddComplianceModule(builder.Configuration);
```

Không chứa business rule trong `Program.cs` hoặc extension đăng ký DI.

---

## 11. Frontend architecture

React cũng tổ chức theo feature/module, không theo một folder components toàn cục.

```text
frontend/apps/admin-web/src/
├─ app/
│  ├─ router/
│  ├─ providers/
│  ├─ layouts/
│  └─ bootstrap.tsx
├─ features/
│  ├─ identity/
│  ├─ students/
│  ├─ classes/
│  ├─ scheduling/
│  ├─ attendance/
│  ├─ finance/
│  ├─ payroll/
│  └─ compliance/
├─ entities/
├─ widgets/
├─ shared/
│  ├─ api/
│  ├─ auth/
│  ├─ ui/
│  ├─ forms/
│  ├─ validation/
│  └─ i18n/
└─ main.tsx
```

Mỗi feature:

```text
features/attendance/
├─ api/
├─ model/
├─ routes/
├─ ui/
├─ validation/
└─ index.ts
```

Chỉ public qua `index.ts`; feature không import file internal của feature khác.

---

## 12. Testing architecture

### 12.1. Test trong từng module

```text
Modules/Finance/tests/
├─ Finance.Domain.Tests/
├─ Finance.Application.Tests/
├─ Finance.Infrastructure.IntegrationTests/
└─ Finance.Api.IntegrationTests/
```

- Domain test không cần database.
- Application test dùng fake ports.
- Infrastructure test dùng PostgreSQL thật qua container.
- API test dùng WebApplicationFactory + PostgreSQL.

### 12.2. Architecture tests

Tự động fail build nếu:

- Domain tham chiếu EF Core/ASP.NET.
- Application tham chiếu Infrastructure/API.
- Module A tham chiếu Domain/Infrastructure của module B.
- Controller inject DbContext/repository.
- Repository trả Request/Response contract.
- Entity bị expose trong public API.

### 12.3. Contract tests

- OpenAPI breaking-change detection.
- JSON và XML serialization.
- Response envelope.
- Webhook/provider contracts.
- Integration event compatibility.

---

## 13. Docker và deployment flow

```text
postgres
  -> db-migrator (run-to-completion)
      -> api
      -> worker
      -> frontend
```

Containers:

- `tutor-postgres`.
- `tutor-db-migrator`.
- `tutor-api`.
- `tutor-worker`.
- `tutor-web`.

Optional:

- Redis cho cache/rate limit/distributed lock.
- Object storage cho tài liệu.
- OpenTelemetry collector.

Secrets không nằm trong source hoặc image. Environment variables chỉ là injection mechanism; production dùng secret manager.

---

## 14. Coding rules bắt buộc

### 14.1. Domain/Application

- Aggregate tự bảo vệ state bằng method, không public setter tùy tiện.
- Handler giải quyết một use case.
- Handler không trả EF entity.
- Không dùng `IServiceProvider` service locator trong nghiệp vụ.
- Không generic repository cho mọi entity.
- Không dùng domain event để che giấu flow đồng bộ quan trọng.

### 14.2. Infrastructure

- Không chứa business decision.
- Không expose `IQueryable` qua module boundary.
- Query string chỉ map qua whitelist expression.
- External call có timeout, retry có điều kiện và circuit breaker.
- Webhook/idempotent command có deduplication.

### 14.3. API

- Controller mỏng.
- Không inject DbContext/repository.
- Request/Response model riêng.
- Global exception handling.
- Authorization ở boundary và object-level trong use case.
- Không log secret/PII không cần thiết.

### 14.4. Database

- UTC/timestamptz.
- VND dùng `bigint` minor unit hoặc `numeric`, không float/double.
- Unique/index/check constraints bảo vệ rule dữ liệu cơ bản.
- Optimistic concurrency cho dữ liệu dễ tranh chấp.
- Financial record đã post không xóa; dùng reversal/void/refund.

---

## 15. Thứ tự triển khai solution

### Phase 1 - Platform skeleton

1. Hosts, BuildingBlocks và module template.
2. Response envelope, exception/logging middleware.
3. API versioning, JSON/XML, Swagger.
4. PostgreSQL, DbMigrator, Docker Compose.
5. Architecture tests.

### Phase 2 - Identity + Organizations

1. Tenant/campus context.
2. User/password/JWT/refresh token.
3. Role/permission/scope.
4. Audit/security events.

### Phase 3 - Academics

1. Student/guardian/teacher profile.
2. Subject/class/enrollment.
3. Search/sort/paging/fields/expand.
4. Imports.

### Phase 4 - Learning Operations

1. Timetable/session/room conflicts.
2. Attendance.
3. Session completion event/outbox.

### Phase 5 - Finance + HR

1. Fee plan/charge/payment/receipt/refund.
2. Expense/reconciliation.
3. Teacher earning/payroll.

### Phase 6 - Compliance + Reporting

1. Disclosure/consent/legal checklist.
2. Tax rule version/draft.
3. Reporting read models/dashboard/export.

---

## 16. Khi nào mới tách microservice

Chỉ tách khi có ít nhất một lý do đo được:

- Module cần deploy độc lập với tần suất khác hẳn.
- Module có profile tải khác rõ rệt, ví dụ xử lý đề thi/file nặng.
- Đội riêng sở hữu end-to-end.
- Isolation bảo mật/pháp lý bắt buộc.
- Monolith đã có boundary tốt và việc tách giảm chi phí thực tế.

Không tách vì “nhiều folder”, “nhiều bảng” hoặc muốn dùng công nghệ mới.

Module dễ là ứng viên tách trước:

1. Documents/Exam processing.
2. Notifications.
3. Reporting/analytics.

Finance, Attendance và Payroll không nên tách sớm vì liên quan consistency nghiệp vụ.

---

## 17. Cấu trúc tối thiểu nên scaffold ngay

Không cần tạo đủ mọi project rỗng từ ngày đầu. Scaffold:

```text
Hosts/
├─ TutorManagement.Api
├─ TutorManagement.Worker
└─ TutorManagement.DbMigrator

BuildingBlocks/
├─ SharedKernel
├─ Application.Abstractions
├─ Infrastructure
└─ WebApi

Modules/
├─ IdentityAccess/{Domain,Application,Infrastructure,Contracts,Api}
├─ Organizations/{Domain,Application,Infrastructure,Contracts,Api}
└─ Academics/{Domain,Application,Infrastructure,Contracts,Api}

tests/
├─ ArchitectureTests
└─ IntegrationTests
```

Chỉ tạo Finance/LearningOperations/HR khi bắt đầu phát triển epic tương ứng. Module template giúp tạo cấu trúc nhất quán.

---

## 18. Kết luận

Kiến trúc chính thức không bị trói bởi folder/project structure của LAB. Các nguyên tắc từ LAB được giữ ở API và code quality, còn cấu trúc hệ thống được tối ưu cho sản phẩm thật:

- Module-first thay vì layer-first toàn solution.
- Clean Architecture trong từng bounded context.
- Vertical Slice thay cho các service khổng lồ.
- Schema/DbContext/migration ownership theo module.
- API host, worker và migrator tách vai trò vận hành.
- Cross-module contracts + outbox/inbox thay vì đọc chéo database.
- Compile-time boundaries và architecture tests chống suy thoái kiến trúc.

Đây là cấu trúc phù hợp để bắt đầu monolith, tăng quy mô đội/codebase và vẫn giữ khả năng tách dịch vụ sau này mà không phải viết lại toàn hệ thống.
