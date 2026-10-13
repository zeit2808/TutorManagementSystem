# Kiến trúc hệ thống Monolith - Tutor Management System

> **Tài liệu đã được thay thế:** Bản này giữ lại để đối chiếu cách ánh xạ LAB. Kiến trúc production chính thức nằm tại [KIEN_TRUC_MODULAR_MONOLITH_THUC_TE.md](KIEN_TRUC_MODULAR_MONOLITH_THUC_TE.md). Hai PDF chỉ được dùng làm checklist quy tắc API, không dùng để áp đặt project/folder structure.

**Phiên bản:** 1.0  
**Ngày:** 13/10/2026  
**Mục tiêu:** Thiết kế ASP.NET Core monolith nhiều chức năng, phân tách rõ nghiệp vụ, dùng PostgreSQL + EF Core migrations, đồng thời đáp ứng LAB 1 và LAB 2 PRN232.

---

## 1. Quyết định kiến trúc

Hệ thống dùng **Modular Monolith + Clean Architecture + Vertical Slice theo use case**.

Ba tầng theo yêu cầu LAB vẫn được giữ nguyên:

| Tầng LAB | Project tương ứng | Trách nhiệm |
|---|---|---|
| API Layer | `API` + `Contracts` | HTTP, routing, binding, content negotiation, authentication, response |
| Service Layer | `Services` + `Domain` | Điều phối use case và toàn bộ quy tắc nghiệp vụ |
| Repository Layer | `Repositories` | PostgreSQL, EF Core, truy vấn và persistence |

Hai project `Domain` và `Contracts` là phần tách nhỏ bên trong ba tầng trên, không tạo thêm process hay microservice. Khi chạy production, toàn hệ thống vẫn là một API monolith, một database PostgreSQL và các background worker cùng codebase.

### 1.1. Vì sao không dùng lớp Service khổng lồ

Không tạo các lớp kiểu:

```text
StudentService.cs       // 2.000 dòng, CRUD + học phí + điểm danh
ClassService.cs         // hàng chục dependency
PaymentService.cs       // vừa validate, vừa query, vừa gửi thông báo
```

Thay vào đó, mỗi hành động là một use case độc lập:

```text
CreateStudentHandler
UpdateStudentHandler
EnrollStudentHandler
RecordAttendanceHandler
CollectTuitionPaymentHandler
CalculateTeacherPayrollHandler
```

Mỗi handler chỉ điều phối một luồng. Quy tắc tính toán nằm trong domain entity, value object, policy hoặc domain service. Repository chỉ lưu/đọc dữ liệu.

### 1.2. Nguyên tắc phụ thuộc

```text
Contracts  ────────────────────────────────┐
                                           │
Domain  <──── Services <──── API            │
   ▲                         │              │
   └──── Repositories <──────┘              │
                  API ──────────────────────┘
```

Quy tắc bắt buộc:

- `Domain` không tham chiếu project nào.
- `Contracts` không tham chiếu `Domain`, `Services` hoặc `Repositories`.
- `Services` chỉ tham chiếu `Domain` và `Contracts`.
- `Repositories` chỉ tham chiếu `Domain`.
- `API` tham chiếu `Services`, `Repositories`, `Contracts` để làm composition root.
- `Services` tuyệt đối không tham chiếu `Repositories`; Dependency Inversion qua interface/port.
- `Repositories` không sử dụng Request/Response Model.
- API không trả Entity Model trực tiếp.

---

## 2. Solution và project structure

```text
TutorManagementSystem/
├─ TutorManagementSystem.sln
├─ Directory.Build.props
├─ Directory.Packages.props
├─ .editorconfig
├─ .gitignore
├─ .env.example
├─ README.md
├─ docker-compose.yml
│
├─ src/
│  ├─ PRN232.TutorManagementSystem.API/
│  │  ├─ Controllers/
│  │  │  ├─ V1/
│  │  │  │  ├─ AuthController.cs
│  │  │  │  ├─ StudentsController.cs
│  │  │  │  ├─ SemestersController.cs
│  │  │  │  ├─ SubjectsController.cs
│  │  │  │  ├─ CoursesController.cs
│  │  │  │  └─ EnrollmentsController.cs
│  │  │  └─ V2/
│  │  │     └─ StudentsController.cs
│  │  ├─ Authentication/
│  │  │  ├─ JwtOptions.cs
│  │  │  ├─ JwtTokenGenerator.cs
│  │  │  ├─ CurrentUser.cs
│  │  │  └─ ClaimsPrincipalExtensions.cs
│  │  ├─ Authorization/
│  │  │  ├─ Policies.cs
│  │  │  └─ PermissionAuthorizationHandler.cs
│  │  ├─ Middleware/
│  │  │  ├─ ExceptionHandlingMiddleware.cs
│  │  │  ├─ RequestLoggingMiddleware.cs
│  │  │  └─ CorrelationIdMiddleware.cs
│  │  ├─ Filters/
│  │  │  └─ ValidateModelStateFilter.cs
│  │  ├─ Extensions/
│  │  │  ├─ ApiServiceCollectionExtensions.cs
│  │  │  ├─ AuthenticationExtensions.cs
│  │  │  ├─ OpenApiExtensions.cs
│  │  │  └─ MiddlewareExtensions.cs
│  │  ├─ Conventions/
│  │  │  └─ ApiResponseConvention.cs
│  │  ├─ Properties/
│  │  │  └─ launchSettings.json
│  │  ├─ appsettings.json
│  │  ├─ appsettings.Development.json
│  │  ├─ Dockerfile
│  │  ├─ Program.cs
│  │  └─ PRN232.TutorManagementSystem.API.csproj
│  │
│  ├─ PRN232.TutorManagementSystem.Contracts/
│  │  ├─ Common/
│  │  │  ├─ ApiResponse.cs
│  │  │  ├─ ApiError.cs
│  │  │  ├─ PaginationMetadata.cs
│  │  │  ├─ PagedResponse.cs
│  │  │  └─ SelectedFieldsResponse.cs
│  │  ├─ Authentication/
│  │  │  ├─ LoginRequest.cs
│  │  │  ├─ LoginResponse.cs
│  │  │  ├─ RefreshTokenRequest.cs
│  │  │  └─ TokenResponse.cs
│  │  ├─ Students/
│  │  │  ├─ CreateStudentRequest.cs
│  │  │  ├─ UpdateStudentRequest.cs
│  │  │  ├─ StudentQueryRequest.cs
│  │  │  ├─ StudentResponse.cs
│  │  │  └─ StudentDetailResponse.cs
│  │  ├─ Semesters/
│  │  ├─ Subjects/
│  │  ├─ Courses/
│  │  └─ Enrollments/
│  │
│  ├─ PRN232.TutorManagementSystem.Domain/
│  │  ├─ Common/
│  │  │  ├─ Entity.cs
│  │  │  ├─ AggregateRoot.cs
│  │  │  ├─ ValueObject.cs
│  │  │  ├─ IDomainEvent.cs
│  │  │  ├─ DomainException.cs
│  │  │  └─ BusinessRuleViolationException.cs
│  │  ├─ Semesters/
│  │  │  ├─ Semester.cs
│  │  │  ├─ ISemesterRepository.cs
│  │  │  └─ Specifications/
│  │  │     └─ ActiveSemesterSpecification.cs
│  │  ├─ Subjects/
│  │  │  ├─ Subject.cs
│  │  │  ├─ SubjectCode.cs
│  │  │  └─ ISubjectRepository.cs
│  │  ├─ Courses/
│  │  │  ├─ Course.cs
│  │  │  ├─ ICourseRepository.cs
│  │  │  └─ Policies/
│  │  │     └─ CourseCapacityPolicy.cs
│  │  ├─ Students/
│  │  │  ├─ Student.cs
│  │  │  ├─ StudentEmail.cs
│  │  │  └─ IStudentRepository.cs
│  │  ├─ Enrollments/
│  │  │  ├─ Enrollment.cs
│  │  │  ├─ EnrollmentStatus.cs
│  │  │  ├─ IEnrollmentRepository.cs
│  │  │  ├─ Policies/
│  │  │  │  └─ EnrollmentEligibilityPolicy.cs
│  │  │  └─ Events/
│  │  │     └─ StudentEnrolledDomainEvent.cs
│  │  └─ Identity/
│  │     ├─ User.cs
│  │     ├─ UserRole.cs
│  │     ├─ RefreshToken.cs
│  │     └─ IUserRepository.cs
│  │
│  ├─ PRN232.TutorManagementSystem.Services/
│  │  ├─ Abstractions/
│  │  │  ├─ Messaging/
│  │  │  │  ├─ ICommand.cs
│  │  │  │  ├─ ICommandHandler.cs
│  │  │  │  ├─ IQuery.cs
│  │  │  │  └─ IQueryHandler.cs
│  │  │  ├─ Persistence/
│  │  │  │  ├─ IUnitOfWork.cs
│  │  │  │  └─ ITransactionManager.cs
│  │  │  ├─ Security/
│  │  │  │  ├─ IPasswordHashService.cs
│  │  │  │  ├─ ITokenGenerator.cs
│  │  │  │  └─ ICurrentUser.cs
│  │  │  └─ Time/
│  │  │     └─ IClock.cs
│  │  ├─ Common/
│  │  │  ├─ Behaviors/
│  │  │  │  ├─ ValidationBehavior.cs
│  │  │  │  ├─ TransactionBehavior.cs
│  │  │  │  └─ LoggingBehavior.cs
│  │  │  ├─ Exceptions/
│  │  │  │  ├─ NotFoundException.cs
│  │  │  │  ├─ ConflictException.cs
│  │  │  │  ├─ ValidationException.cs
│  │  │  │  └─ UnauthorizedException.cs
│  │  │  ├─ Models/
│  │  │  │  ├─ Result.cs
│  │  │  │  ├─ PagedResult.cs
│  │  │  │  └─ Error.cs
│  │  │  ├─ Querying/
│  │  │  │  ├─ PageRequest.cs
│  │  │  │  ├─ SortTerm.cs
│  │  │  │  ├─ FieldSelection.cs
│  │  │  │  └─ ExpansionOptions.cs
│  │  │  └─ Mapping/
│  │  ├─ Features/
│  │  │  ├─ Authentication/
│  │  │  │  ├─ Login/
│  │  │  │  │  ├─ LoginCommand.cs
│  │  │  │  │  ├─ LoginCommandValidator.cs
│  │  │  │  │  ├─ LoginCommandHandler.cs
│  │  │  │  │  └─ LoginResult.cs
│  │  │  │  └─ RefreshToken/
│  │  │  ├─ Students/
│  │  │  │  ├─ BusinessModels/
│  │  │  │  │  ├─ StudentModel.cs
│  │  │  │  │  └─ StudentDetailModel.cs
│  │  │  │  ├─ CreateStudent/
│  │  │  │  │  ├─ CreateStudentCommand.cs
│  │  │  │  │  ├─ CreateStudentCommandValidator.cs
│  │  │  │  │  ├─ StudentCodeValidator.cs
│  │  │  │  │  └─ CreateStudentCommandHandler.cs
│  │  │  │  ├─ UpdateStudent/
│  │  │  │  ├─ GetStudentById/
│  │  │  │  ├─ SearchStudents/
│  │  │  │  └─ DeleteStudent/
│  │  │  ├─ Semesters/
│  │  │  ├─ Subjects/
│  │  │  ├─ Courses/
│  │  │  │  └─ GetCourseStudents/
│  │  │  └─ Enrollments/
│  │  │     ├─ BusinessModels/
│  │  │     ├─ EnrollStudent/
│  │  │     ├─ ChangeEnrollmentStatus/
│  │  │     ├─ GetEnrollmentById/
│  │  │     └─ SearchEnrollments/
│  │  └─ DependencyInjection.cs
│  │
│  └─ PRN232.TutorManagementSystem.Repositories/
│     ├─ Persistence/
│     │  ├─ AppDbContext.cs
│     │  ├─ DesignTimeDbContextFactory.cs
│     │  ├─ UnitOfWork.cs
│     │  ├─ Configurations/
│     │  │  ├─ SemesterConfiguration.cs
│     │  │  ├─ SubjectConfiguration.cs
│     │  │  ├─ CourseConfiguration.cs
│     │  │  ├─ StudentConfiguration.cs
│     │  │  ├─ EnrollmentConfiguration.cs
│     │  │  ├─ UserConfiguration.cs
│     │  │  └─ RefreshTokenConfiguration.cs
│     │  ├─ Migrations/
│     │  └─ Seed/
│     │     ├─ DevelopmentDataSeeder.cs
│     │     └─ SeedDataOptions.cs
│     ├─ Repositories/
│     │  ├─ SemesterRepository.cs
│     │  ├─ SubjectRepository.cs
│     │  ├─ CourseRepository.cs
│     │  ├─ StudentRepository.cs
│     │  ├─ EnrollmentRepository.cs
│     │  └─ UserRepository.cs
│     ├─ Querying/
│     │  ├─ StudentQueryBuilder.cs
│     │  ├─ EnrollmentQueryBuilder.cs
│     │  ├─ SortMap.cs
│     │  └─ ProjectionBuilder.cs
│     └─ DependencyInjection.cs
│
├─ tests/
│  ├─ PRN232.TutorManagementSystem.Domain.Tests/
│  ├─ PRN232.TutorManagementSystem.Services.Tests/
│  ├─ PRN232.TutorManagementSystem.Repositories.Tests/
│  ├─ PRN232.TutorManagementSystem.API.IntegrationTests/
│  └─ PRN232.TutorManagementSystem.ArchitectureTests/
│
└─ docs/
   ├─ adr/
   ├─ api/
   └─ database/
```

### 2.1. Nếu giảng viên chỉ cho đúng ba project

Giữ nguyên ba project bắt buộc và di chuyển folder:

```text
Domain/*     -> Services/Domain/*
Contracts/*  -> API/Contracts/*
```

Mọi quy tắc và cấu trúc còn lại không đổi. Phương án năm project tốt hơn vì compiler ngăn phụ thuộc sai, nhưng phương án ba project vẫn đáp ứng tổ chức logic.

---

## 3. Bốn loại model theo LAB

### 3.1. Entity Model

Vị trí: `Domain/<Module>/*.cs`.

Đặc điểm:

- Đại diện trạng thái và hành vi cốt lõi.
- EF Core map bằng Fluent Configuration trong `Repositories`.
- Không gắn `[JsonIgnore]`, `[FromBody]` hoặc response concern.
- Không trả trực tiếp từ controller.
- Property setter ưu tiên `private` để buộc thay đổi qua method nghiệp vụ.

Ví dụ:

```csharp
public sealed class Enrollment : AggregateRoot<int>
{
    public int StudentId { get; private set; }
    public int CourseId { get; private set; }
    public DateTime EnrollDateUtc { get; private set; }
    public EnrollmentStatus Status { get; private set; }

    private Enrollment() { }

    public static Enrollment Create(
        int studentId,
        int courseId,
        DateTime enrolledAtUtc)
    {
        if (studentId <= 0) throw new BusinessRuleViolationException("Invalid student.");
        if (courseId <= 0) throw new BusinessRuleViolationException("Invalid course.");

        return new Enrollment(studentId, courseId, enrolledAtUtc);
    }

    public void Cancel()
    {
        if (Status == EnrollmentStatus.Completed)
            throw new BusinessRuleViolationException("Completed enrollment cannot be cancelled.");

        Status = EnrollmentStatus.Cancelled;
    }
}
```

### 3.2. Business Model

Vị trí: `Services/Features/<Module>/BusinessModels` hoặc result của từng use case.

- Dữ liệu đã xử lý dùng bên trong tầng nghiệp vụ.
- Không phụ thuộc HTTP.
- Có thể chứa kết quả tính toán, projection hoặc thông tin tổng hợp nhiều entity.

### 3.3. Request Model

Vị trí: `Contracts/<Module>`.

- Chỉ mô tả input từ client.
- Có Data Annotation theo yêu cầu LAB.
- Không chứa logic database.
- FluentValidation thực hiện rule phức tạp/cross-field.

```csharp
public sealed class CreateStudentRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^(SE|CE)\\d{5}$")]
    public string StudentCode { get; init; } = string.Empty;

    public DateTime DateOfBirth { get; init; }
}
```

### 3.4. Response Model

Vị trí: `Contracts/<Module>`.

- Chỉ chứa dữ liệu công khai qua API.
- Không chứa navigation property hai chiều.
- Không trả password hash, refresh token hash hoặc trường audit nội bộ.
- Có response chi tiết riêng cho API by ID và response gọn cho list.

---

## 4. Logic đặt ở đâu

| Loại logic | Vị trí đúng | Ví dụ |
|---|---|---|
| Invariant của một entity | Domain Entity | Enrollment completed không thể cancel |
| Giá trị có quy tắc riêng | Value Object | Email, SubjectCode, Money |
| Rule dùng nhiều entity nhưng thuần nghiệp vụ | Domain Policy/Domain Service | Kiểm tra đủ điều kiện ghi danh |
| Điều phối một use case | Command/Query Handler | Load course + student, gọi policy, save |
| Validation input hình thức | Request Model | Required, EmailAddress, Range |
| Validation nghiệp vụ cần DB | Handler/Policy | Email đã tồn tại, course đã đầy |
| Query/filter/sort SQL | Repository/QueryBuilder | Search theo tên, sort whitelist |
| HTTP mapping/status/header | Controller | 201 + Location, 404, Accept header |
| Mapping API contract | API/Services mapping | Request -> Command, Model -> Response |
| Transaction | Application behavior + UnitOfWork | Enrollment + audit cùng transaction |
| JWT/hash/config | Technical adapter | Generate token, hash password |

### 4.1. Handler mẫu

```csharp
public sealed class EnrollStudentCommandHandler
    : ICommandHandler<EnrollStudentCommand, EnrollmentModel>
{
    public async Task<Result<EnrollmentModel>> Handle(
        EnrollStudentCommand command,
        CancellationToken cancellationToken)
    {
        var student = await studentRepository.GetByIdAsync(command.StudentId, cancellationToken);
        var course = await courseRepository.GetByIdAsync(command.CourseId, cancellationToken);

        eligibilityPolicy.EnsureCanEnroll(student, course, command.EnrollDateUtc);

        var enrollment = Enrollment.Create(
            student.Id,
            course.Id,
            command.EnrollDateUtc);

        enrollmentRepository.Add(enrollment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return enrollmentMapper.ToModel(enrollment);
    }
}
```

Handler lý tưởng khoảng 20-60 dòng. Nếu dài hơn, kiểm tra xem rule có nên chuyển về entity/policy hoặc query builder không.

Repository chỉ load aggregate, query/projection và persistence; không quyết định học sinh có được ghi danh hay không, không tạo HTTP response.

---

## 5. Luồng xử lý request

### 5.1. Command flow

```text
HTTP Request
  -> Controller
  -> Request Model binding + Data Annotations
  -> Command
  -> ValidationBehavior / FluentValidation
  -> Command Handler
  -> Domain Entity / Policy
  -> Repository interface
  -> EF Repository implementation
  -> UnitOfWork transaction
  -> Business Model
  -> Response Model
  -> ApiResponse<T>
```

### 5.2. Query flow

```text
GET /api/v1/students?...options
  -> StudentQueryRequest
  -> SearchStudentsQuery
  -> Query Handler
  -> Repository QueryBuilder
  -> IQueryable with whitelisted filters/sorts/expands
  -> Projection at database level
  -> PagedResult<StudentModel>
  -> PagedResponse<StudentResponse>
```

Query không load toàn bộ bảng rồi mới filter trong memory.

### 5.3. Controller mỏng

Controller chỉ nhận model binding, tạo command/query, gọi handler/dispatcher, map response và chọn HTTP status/header.

Không đặt tại controller:

- Kiểm tra trùng email bằng DbContext.
- Tính eligibility.
- Hash password.
- Tạo JWT thủ công.
- `try/catch` lặp ở từng action.

---

## 6. Phân module khi mở rộng ứng dụng dạy thêm

```text
Domain/
├─ Identity/
├─ Organizations/
├─ Students/
├─ Guardians/
├─ Teachers/
├─ Subjects/
├─ Classes/
├─ Enrollments/
├─ Scheduling/
├─ Attendance/
├─ Tuition/
├─ Payments/
├─ Expenses/
├─ Payroll/
├─ Reporting/
└─ TaxCompliance/
```

Mỗi module có namespace và feature slices riêng. Không cho module gọi repository của module khác tùy tiện. Use case liên module gọi abstraction hoặc domain/application event.

```text
CompleteTeachingSessionHandler
  -> Session.Complete()
  -> SessionCompletedDomainEvent
      -> AttendanceFinalizationHandler
      -> TuitionChargeHandler
      -> TeacherEarningHandler
```

Trong LAB có thể xử lý đồng bộ cùng transaction. Khi production cần async, dùng Outbox nhưng vẫn giữ monolith.

---

## 7. PostgreSQL và EF Core

### 7.1. DbContext

```csharp
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention());
```

### 7.2. Schema đề xuất

```text
academic.semesters
academic.subjects
academic.courses
student.students
student.enrollments
identity.users
identity.refresh_tokens
audit.audit_logs
```

Scope LAB có thể dùng schema `public`; production nên tách schema theo module.

### 7.3. Mapping quan hệ LAB

```text
Semester 1 ─── * Course
Subject  1 ─── * Course
Student  1 ─── * Enrollment
Course   1 ─── * Enrollment
User     1 ─── * RefreshToken
```

PDF LAB khai báo `Course` chỉ có `CourseId`, `CourseName`, `SemesterId` trong khi còn bảng `Subject`. Nên bổ sung `SubjectId` vào `Course`; đề cho phép thêm bảng/cột khi cần.

### 7.4. Constraint và index

- Unique `students.email`.
- Unique `subjects.subject_code`.
- Unique composite `enrollments(student_id, course_id)`.
- Check `subjects.credit > 0`.
- Check `semesters.start_date < semesters.end_date`.
- Index `courses.semester_id`, `courses.subject_id`.
- Index `enrollments.course_id`, `enrollments.status`, `enrollments.enroll_date`.
- Unique `users.username`.
- Unique `refresh_tokens.token_hash`.
- Timestamp lưu UTC bằng `timestamp with time zone`.
- Dùng concurrency token cho aggregate hay cập nhật đồng thời.

### 7.5. Migration commands

```powershell
dotnet ef migrations add InitialCreate `
  --project src/PRN232.TutorManagementSystem.Repositories `
  --startup-project src/PRN232.TutorManagementSystem.API `
  --context AppDbContext `
  --output-dir Persistence/Migrations
```

```powershell
dotnet ef database update `
  --project src/PRN232.TutorManagementSystem.Repositories `
  --startup-project src/PRN232.TutorManagementSystem.API
```

```powershell
dotnet ef migrations script --idempotent `
  --project src/PRN232.TutorManagementSystem.Repositories `
  --startup-project src/PRN232.TutorManagementSystem.API `
  --output artifacts/database/migrate.sql
```

### 7.6. Quy tắc migration

- Không sửa migration đã chạy trên môi trường dùng chung.
- Mỗi migration giải quyết một thay đổi schema có tên rõ nghĩa.
- Review cả `Up` và `Down`.
- Không để nhiều API replica tự migrate production đồng thời.
- Production chạy migration bằng deployment job riêng.
- Thay đổi phá vỡ dùng expand-and-contract.
- Backup và thử restore trước migration rủi ro cao.

### 7.7. Seed data theo LAB 1

Tối thiểu 5 semesters, 50 students, 10 subjects, 20 courses và 500 enrollments.

Không đưa 500 record bằng `HasData` vào model snapshot. Dùng `DevelopmentDataSeeder` deterministic, có marker/version, không chạy production.

---

## 8. REST API design theo LAB 1

### 8.1. Resource routes

```text
GET    /api/v1/students
POST   /api/v1/students
GET    /api/v1/students/{id}
PUT    /api/v1/students/{id}
DELETE /api/v1/students/{id}

GET    /api/v1/courses/{courseId}/students
POST   /api/v1/courses/{courseId}/enrollments
GET    /api/v1/enrollments/{id}
```

Không dùng `/api/getStudents` hay `/api/createEnrollment`.

### 8.2. GET by ID

- Trả related data qua response model riêng.
- Projection có kiểm soát, không serialize navigation property EF.
- Không có resource trả 404.

### 8.3. List query options

```text
GET /api/v1/students
    ?search=nguyen
    &sort=fullName,-dateOfBirth
    &page=2
    &size=10
    &fields=studentId,fullName,email
    &expand=enrollments
```

- `search`: resource tự định nghĩa field.
- `sort`: map tên API sang expression; không nối SQL.
- `page >= 1`.
- `size` mặc định 20, tối đa 100.
- `fields` và `expand` dùng whitelist.
- Unknown sort/field/expand trả 400.

### 8.4. Pagination metadata và envelope

```json
{
  "success": true,
  "message": "Request processed successfully",
  "data": [],
  "errors": null,
  "pagination": {
    "page": 1,
    "pageSize": 10,
    "totalItems": 100,
    "totalPages": 10
  }
}
```

Status bắt buộc: 200, 201, 400, 404, 500. Nên bổ sung 401, 403, 406, 409 và 204 theo đúng tình huống.

---

## 9. Yêu cầu nâng cao theo LAB 2

### 9.1. Content negotiation JSON/XML

```csharp
builder.Services
    .AddControllers(options =>
    {
        options.ReturnHttpNotAcceptable = true;
        options.RespectBrowserAcceptHeader = true;
    })
    .AddXmlSerializerFormatters();
```

Test:

- `Accept: application/json` -> JSON.
- `Accept: application/xml` -> XML.
- `Accept: application/yaml` -> 406.

### 9.2. Model binding

```csharp
[HttpGet("{id:int}", Name = RouteNames.GetStudentById)]
public async Task<IActionResult> GetById(
    [FromRoute] int id,
    [FromHeader(Name = "X-Request-Id")] string? requestId,
    CancellationToken cancellationToken)

[HttpGet]
public async Task<IActionResult> Search(
    [FromQuery] StudentQueryRequest request,
    CancellationToken cancellationToken)

[HttpPost]
public async Task<IActionResult> Create(
    [FromBody] CreateStudentRequest request,
    CancellationToken cancellationToken)
```

### 9.3. Validation

Ba lớp:

1. Data Annotation trên Request Model.
2. FluentValidation ít nhất một Request/Command.
3. Custom validator cho mã sinh viên `SE19886`, `CE18793`.

Data Annotation xử lý format đơn giản; FluentValidation xử lý cross-field; domain xử lý invariant.

### 9.4. Routing và versioning

```csharp
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/students")]
public sealed class StudentsController : ControllerBase
```

Phải có route constraint, nested resource, named route và tối thiểu v1/v2. Không copy toàn bộ V1 sang V2 nếu contract không thay đổi.

### 9.5. Exception middleware

| Exception | Status |
|---|---:|
| ValidationException | 400 |
| NotFoundException | 404 |
| UnauthorizedException | 401 |
| ForbiddenException | 403 |
| ConflictException | 409 |
| BusinessRuleViolationException | 422 hoặc 400 theo convention |
| Unknown exception | 500 |

500 không trả stack trace/SQL/secret; log nội bộ kèm correlation ID.

### 9.6. Request logging middleware

Log request ID, method, path, status, execution time và user ID. Không log password, Authorization header, access/refresh token hoặc body đăng nhập.

### 9.7. JWT authentication

```text
POST /api/v1/auth/login
POST /api/v1/auth/refresh-token
POST /api/v1/auth/revoke
```

Quy tắc:

- Password dùng `PasswordHasher<User>` hoặc BCrypt.
- JWT secret lấy từ environment/secret store.
- Access token ngắn hạn.
- Refresh token random, chỉ lưu hash trong DB.
- Mỗi lần refresh phải rotate token.
- Token cũ bị revoke và liên kết token thay thế.
- Reuse token đã revoke thì revoke cả token family/session.
- Login trả `accessToken`, `refreshToken`, `expiresIn`.

### 9.8. Authorization

- `[Authorize]` cho protected endpoint.
- `[Authorize(Roles = "Admin")]` cho ít nhất một admin endpoint.
- Production nên dùng policy/permission cho quyền chi tiết.

### 9.9. Swagger/OpenAPI

Swagger phải có endpoint/version, schema, status codes, Bearer security scheme và nút Authorize để test JWT.

---

## 10. Authentication database model

```text
users
├─ user_id int PK
├─ username varchar(50) unique not null
├─ password_hash varchar(255) not null
├─ role varchar(20) not null
├─ is_active boolean not null
├─ created_at_utc timestamptz not null
└─ updated_at_utc timestamptz not null

refresh_tokens
├─ refresh_token_id uuid PK
├─ user_id int FK
├─ token_hash varchar(255) unique not null
├─ token_family_id uuid not null
├─ expires_at_utc timestamptz not null
├─ created_at_utc timestamptz not null
├─ revoked_at_utc timestamptz null
└─ replaced_by_token_id uuid null
```

Audit log nên có user, action, resource, time, correlation ID và metadata `jsonb`; không ghi password/token.

---

## 11. Docker deployment

```text
tutor-api       ASP.NET Core API
tutor-db        PostgreSQL
```

```yaml
services:
  db:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: tutor_management
      POSTGRES_USER: tutor_app
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U tutor_app -d tutor_management"]

  api:
    build:
      context: .
      dockerfile: src/PRN232.TutorManagementSystem.API/Dockerfile
    environment:
      ConnectionStrings__DefaultConnection: ${DB_CONNECTION_STRING}
      Jwt__Secret: ${JWT_SECRET}
      Jwt__Issuer: PRN232.TutorManagementSystem
      Jwt__Audience: PRN232.TutorManagementSystem.Client
    depends_on:
      db:
        condition: service_healthy
    ports:
      - "8080:8080"

volumes:
  postgres_data:
```

Không commit `.env`; chỉ commit `.env.example` với placeholder.

---

## 12. Dependency Injection

```csharp
builder.Services
    .AddApi(configuration)
    .AddApplication()
    .AddRepositories(configuration);
```

`Services` đăng ký handlers, validators, policies, mapping và behaviors. `Repositories` đăng ký DbContext, repository implementations, UnitOfWork và query builders. `API` đăng ký controllers/XML/versioning/JWT/Swagger/middleware.

Không gọi `new StudentRepository()` hoặc `new JwtTokenGenerator()` trong controller/handler.

---

## 13. Transaction, concurrency và idempotency

- Mỗi command thay đổi dữ liệu có một transaction boundary.
- Handler save một lần ở cuối hoặc dùng transaction behavior.
- Không gọi API ngoài trong DB transaction kéo dài.
- Attendance/payment/enrollment status dùng optimistic concurrency; conflict trả 409.
- Payment/refund/webhook dùng `Idempotency-Key` và unique business key.

---

## 14. Query implementation

Tạo sort map bằng expression, không nối dynamic SQL. Thứ tự đúng:

```text
IQueryable -> Where -> OrderBy -> Select -> Skip/Take -> ToListAsync
```

Không `ToListAsync` toàn bảng rồi mới filter. Expansion và field selection phải whitelist, giới hạn depth, không expose field nhạy cảm.

---

## 15. Testing structure

### Domain tests

- Semester start trước end.
- Subject credit hợp lệ.
- Enrollment state transition.
- Eligibility policy.
- Value object validation.

### Services tests

- Handler NotFound khi thiếu resource.
- Handler gọi policy và save đúng một lần.
- Validation behavior chặn input sai.

### Repository tests

Dùng PostgreSQL container thật để test mapping, constraints, search/sort/page, transaction và concurrency.

### API integration tests

- JSON/XML/406.
- Route/query/body/header binding.
- Response envelope và status codes.
- JWT login/refresh/revoke.
- Authorize/admin-only.

### Architecture tests

- Domain không tham chiếu EF/ASP.NET.
- Services không tham chiếu Repositories.
- Repositories không tham chiếu Contracts.
- Controller không inject DbContext/repository.
- Repository không trả Request/Response Model.

---

## 16. Coding conventions

- Bật nullable reference types và warnings as errors.
- Async xuyên suốt I/O, truyền `CancellationToken`.
- Suffix rõ: Request, Response, Command, Query, Handler, Validator, Repository.
- Không dùng `Helper`/`Utils` cho nghiệp vụ.
- Không generic repository làm mất ngôn ngữ domain.
- Không expose `IQueryable` ra ngoài Repository layer.
- Không dùng AutoMapper cho rule nghiệp vụ.

---

## 17. Kế hoạch triển khai

### Giai đoạn 1 - Skeleton

1. Tạo solution và năm project.
2. Thiết lập project references.
3. Thêm Npgsql/DbContext.
4. Response envelope, middleware, versioning, Swagger.
5. Dockerfile/docker-compose.

### Giai đoạn 2 - LAB 1 core

1. Domain models Semester, Subject, Course, Student, Enrollment.
2. EF configurations và migration.
3. Seeder đủ dữ liệu.
4. CRUD use cases.
5. Search/sort/page/fields/expand.

### Giai đoạn 3 - LAB 2

1. User/RefreshToken migration.
2. Password hashing.
3. Login/refresh/revoke.
4. JWT/authorization.
5. XML/406.
6. FluentValidation + custom validation.
7. Exception/logging middleware.
8. API v1/v2, nested/named routes.
9. Swagger Authorize.

### Giai đoạn 4 - Production hardening

1. Tests và architecture tests.
2. Health checks/logging/metrics.
3. Secrets.
4. Migration deployment job.
5. Rate limiting, audit và concurrency.

### Giai đoạn 5 - Quản lý dạy thêm

```text
Organizations/Teachers/Students
  -> Classes/Enrollments
  -> Scheduling/Rooms
  -> Attendance
  -> Tuition/Payments
  -> Expenses/Reporting
  -> Payroll
  -> TaxCompliance
```

---

## 18. Checklist đối chiếu LAB

### LAB 1

- [ ] Đúng naming API/Services/Repositories.
- [ ] Controller và Repository không chứa business logic.
- [ ] Entity/Business/Request/Response Models tách biệt.
- [ ] Resource routes, plural nouns.
- [ ] GET by ID có related data, 404, không circular reference.
- [ ] Search/filter/sort/paging/fields/expand.
- [ ] Pagination metadata và consistent envelope.
- [ ] 200/201/400/404/500.
- [ ] API + PostgreSQL chạy Docker Compose.
- [ ] Swagger/OpenAPI.
- [ ] Seed đủ 5/50/10/20/500.

### LAB 2

- [ ] User và RefreshToken.
- [ ] Data Annotation + FluentValidation + custom rule.
- [ ] JSON/XML và 406.
- [ ] Route/query/body/header binding.
- [ ] Attribute/constraint/nested/named routes.
- [ ] API v1/v2.
- [ ] Exception + request logging middleware.
- [ ] JWT login/refresh/validation.
- [ ] Authorize và admin-only endpoint.
- [ ] Password hash, JWT secret từ environment.
- [ ] Swagger Authorize.

---

## 19. Anti-pattern cấm trong code review

```text
Controller -> DbContext trực tiếp
Controller -> Repository trực tiếp
Repository -> ApiResponse/Request Model
Services -> concrete Repository
Entity -> HttpContext/IConfiguration
Handler -> raw SQL từ query string
Một Service chứa toàn bộ CRUD và business flow
Trả EF Entity cùng navigation graph
JWT secret/password trong source control
Refresh token lưu plaintext
Auto migrate production từ mọi API replica
Log Authorization header hoặc login body
```

---

## 20. Kết luận

Kiến trúc này giữ đúng ba tầng của PRN232 nhưng tránh mô hình `Controller -> Big Service -> Generic Repository`:

- Controller mỏng.
- Use case tách theo command/query handler.
- Entity và policy giữ quy tắc nghiệp vụ.
- Repository tập trung persistence/query.
- Contracts cô lập API models.
- Project references bảo vệ dependency bằng compiler.
- PostgreSQL migrations, Docker, Swagger, JWT, XML/JSON, validation và versioning đều có vị trí rõ ràng.

Khi số module tăng, thêm vertical slice trong monolith. Chỉ tách microservice khi có bằng chứng về tải, ownership đội ngũ hoặc nhu cầu deploy độc lập.
