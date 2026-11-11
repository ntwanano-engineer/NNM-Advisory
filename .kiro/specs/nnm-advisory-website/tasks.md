# Implementation Plan: NNM Advisory Company Profile Website

## Overview

This plan converts the design document into incremental coding tasks for a React SPA frontend, a .NET (C#) REST API backend, a PostgreSQL database managed via EF Core migrations, Docker Compose for local development, and AWS infrastructure. Tasks are ordered so each step builds on the previous one, ending with everything wired together. Property-based tests (CsCheck for .NET, fast-check for React) are included as optional sub-tasks aligned to the 11 correctness properties in the design document.

---

## Tasks

- [x] 1. Scaffold repository structure and local development environment
  - Create monorepo directory layout: `/frontend`, `/api`, `/infra`, `/docs`
  - Add root-level `docker-compose.yml` defining `frontend`, `api`, and `db` services as specified in the design (PostgreSQL 16, ports 3000/5000/5432)
  - Add `api/.env.sample` with all required variable names and placeholder values (no real credentials): `DB_CONNECTION_STRING`, `JWT_SECRET`, `EMAIL_PROVIDER`, `SMTP_HOST`, `SMTP_PORT`, `SENDER_EMAIL`, `ADMIN_EMAIL`, `S3_IMAGES_BUCKET`, `IMAGE_STORAGE_PROVIDER`, `CORS_ALLOWED_ORIGIN`
  - Add `.gitignore` entries excluding `api/.env`, all `*.env` files with real values, and build artefacts
  - Create root `README.md` documenting the startup sequence (≤ 5 commands) and expected health-check output
  - _Requirements: 12.1, 12.2, 12.4, 12.5_

- [x] 2. Bootstrap .NET API project and configuration infrastructure
  - [x] 2.1 Initialise .NET Web API project with layered folder structure: `Controllers/`, `Services/`, `Repositories/`, `Models/`, `Infrastructure/`
    - Target .NET 8; add NuGet packages: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `AWSSDK.SimpleEmailV2`, `AWSSDK.S3`, `AWS.Extensions.Configuration.SystemsManager`, `Serilog.AspNetCore`, `BCrypt.Net-Next`, `dotenv.net`, `CsCheck`
    - _Requirements: 10.1, 12.1_
  - [x] 2.2 Implement startup configuration validation using `IOptions<T>` with data annotations
    - Define strongly-typed settings classes: `DatabaseSettings`, `JwtSettings`, `EmailSettings`, `S3Settings`, `CorsSettings`
    - Register validation via `ValidateDataAnnotations()` and `ValidateOnStart()`; application must halt with a descriptive error if any required variable is absent
    - _Requirements: 12.6_
  - [x] 2.3 Configure middleware pipeline: HTTPS enforcement, CORS (single allowed origin from config), Serilog structured JSON request logging, global exception handler returning HTTP 500 with generic message and logging UTC timestamp + request path
    - _Requirements: 10.5, 10.6, 10.8, 13.7_
  - [x] 2.4 Add `HealthController` with `GET /health` returning HTTP 200
    - _Requirements: 13.4, 13.8_

- [x] 3. Database schema and EF Core migrations
  - [x] 3.1 Define EF Core entity classes and `NnmDbContext`: `AdminUser`, `CompanyContent`, `ServiceBlock`, `LeadershipProfile`, `ContactSubmission`, `CustomerSurveyResponse`, `EmployeeSurveyResponse`
    - Apply column constraints matching the SQL schema in the design: UUID PKs, VARCHAR limits, CHECK constraints, `TIMESTAMPTZ` columns, `TEXT[]` for offerings
    - _Requirements: 11.1, 11.2_
  - [x] 3.2 Create and apply initial EF Core migration producing all seven tables
    - Verify applying all migrations to an empty PostgreSQL database produces the complete schema without errors
    - _Requirements: 11.4_
  - [x] 3.3 Write property test for database round-trip persistence
    - **Property 2: Contact Submission Persistence Round-Trip**
    - **Validates: Requirements 5.2, 11.6**
    - Use CsCheck generators for valid contact submission payloads; assert stored record fields equal submitted values and `submitted_at` is a valid UTC timestamp

- [ ] 4. Company profile data layer and read endpoints
  - [-] 4.1 Implement `ICompanyProfileRepository` and `CompanyProfileRepository` using EF Core for `CompanyContent`, `ServiceBlock`, and `LeadershipProfile` entities
    - _Requirements: 10.1, 11.3_
  - [x] 4.2 Implement `ICompanyProfileService` / `CompanyProfileService` with 5-second in-memory TTL cache for profile, services, and leadership reads
    - _Requirements: 3.8_
  - [x] 4.3 Implement `CompanyProfileController` with endpoints `GET /api/profile`, `GET /api/profile/services`, `GET /api/profile/leadership`
    - Responses in JSON; HTTP 200 on success; HTTP 500 with generic message on unhandled error
    - _Requirements: 10.1, 10.2_
  - [x] 4.4 Write unit tests for `CompanyProfileService` cache behaviour
    - Assert that a second call within 5 seconds returns cached data without hitting the repository
    - _Requirements: 3.8_

- [x] 5. Contact form submission — backend
  - [x] 5.1 Implement `IContactRepository` / `ContactRepository` using EF Core with parameterised queries via EF Core ORM
    - _Requirements: 11.3, 11.5_
  - [x] 5.2 Implement `IContactService` / `ContactService` with field-level validation: full name ≤ 100 chars, email `local-part@domain.tld`, subject ≤ 150 chars, message 10–2000 chars; persist submission in a transaction; trigger email send after successful persist
    - _Requirements: 5.2, 5.3, 5.6, 5.7_
  - [x] 5.3 Implement `ContactController` with `POST /api/contact`
    - Return HTTP 202 on success; HTTP 422 with per-field error map on validation failure; HTTP 500 on database error
    - _Requirements: 10.2, 10.3, 10.4_
  - [x] 5.4 Write property test for contact form validation
    - **Property 1: Contact Form Validation Rejects Structurally Invalid Inputs**
    - **Validates: Requirements 5.6, 5.7, 10.4**
    - Generate strings outside valid length bounds and malformed emails; assert API returns HTTP 422 and database row count is unchanged
  - [x] 5.5 Write property test for contact submission round-trip
    - **Property 2: Contact Submission Persistence Round-Trip**
    - **Validates: Requirements 5.2, 11.6**
    - Generate valid payloads; assert stored record fields equal submitted values and `submitted_at` is UTC

- [x] 6. Survey submission — backend
  - [x] 6.1 Implement `ISurveyRepository` / `SurveyRepository` for `CustomerSurveyResponse` and `EmployeeSurveyResponse` entities
    - Employee survey repository MUST NOT write any PII fields (no name, email, employee ID columns exist in schema)
    - _Requirements: 7.6, 11.1_
  - [x] 6.2 Implement `ISurveyService` / `SurveyService`
    - Customer: validate `service_satisfaction` ∈ [1,5], `nps_score` ∈ [0,10], `open_feedback` ≤ 1000 chars
    - Employee: validate required questions present; strip any PII from payload before persist
    - _Requirements: 6.3, 6.4, 7.3, 7.4, 7.6_
  - [x] 6.3 Implement `SurveyController` with `POST /api/surveys/customer` and `POST /api/surveys/employee`
    - Return HTTP 202 on success; HTTP 422 on validation failure; HTTP 500 on database error
    - _Requirements: 10.2, 10.4_
  - [x] 6.4 Write property test for survey score validation
    - **Property 6: Survey Validation Rejects Incomplete Submissions**
    - **Validates: Requirements 6.4, 7.4**
    - Generate scores outside valid ranges; assert API returns HTTP 422 and survey row count is unchanged
  - [x] 6.5 Write property test for survey timestamp invariant
    - **Property 7: Survey Timestamp Invariant**
    - **Validates: Requirements 6.3, 7.3, 11.6**
    - Generate valid survey payloads; assert `submitted_at` is within 5-second window of API receipt
  - [x] 6.6 Write property test for employee survey anonymity
    - **Property 11: Employee Survey Anonymity Invariant**
    - **Validates: Requirements 7.6**
    - Generate payloads containing name/email/id fields; assert stored record contains none of those fields

- [ ] 7. Email service
  - [-] 7.1 Define `IEmailService` interface with `SendAcknowledgementEmailAsync` and `SendAdminNotificationEmailAsync`
    - _Requirements: 5.3, 5.4, 5.5_
  - [x] 7.2 Implement `AwsSesEmailService` using `AWSSDK.SimpleEmailV2`
    - Acknowledgement email: personalised greeting with client full name, confirms receipt
    - Admin notification email: includes client full name, email, subject, and message body
    - _Requirements: 5.4, 5.5_
  - [x] 7.3 Implement `SmtpEmailService` for local development (connects to Mailhog on `SMTP_HOST:SMTP_PORT`)
    - Register correct implementation via `EMAIL_PROVIDER` environment variable at startup
    - _Requirements: 12.2_
  - [x] 7.4 Write unit tests for email service
    - Assert acknowledgement email contains client name
    - Assert admin notification email contains all four required fields (name, email, subject, message)
    - _Requirements: 5.4, 5.5_

- [ ] 8. Image storage service
  - [-] 8.1 Define `IImageStorageService` interface with `UploadImageAsync` and `GetPresignedUrlAsync`
    - _Requirements: 4.5, 9.3_
  - [x] 8.2 Implement `S3ImageStorageService`
    - Validate MIME type (JPEG/PNG only) and file size (≤ 5 MB) before upload; return S3 object key on success
    - Generate pre-signed GET URLs valid for 1 hour
    - Use ECS task role (no access keys); bucket name from `S3Settings`
    - _Requirements: 4.5, 4.6, 9.3, 9.4, 9.5_
  - [x] 8.3 Implement local-dev image storage provider (filesystem under `IMAGE_STORAGE_PROVIDER=local`) and register via config at startup
    - _Requirements: 12.2_
  - [x] 8.4 Write property test for file upload rejection
    - **Property 5: Profile Photo Upload Type and Size Rejection**
    - **Validates: Requirements 4.5, 4.6, 9.3, 9.4, 9.5**
    - Generate byte arrays with non-JPEG/PNG MIME types and files exceeding 5 MB; assert API returns HTTP 400 and S3 bucket object count is unchanged

- [ ] 9. Admin authentication
  - [-] 9.1 Implement `AdminUser` repository and seed a default admin user with a bcrypt-hashed password on first migration
    - `PasswordHash` stored as bcrypt hash; plaintext never persisted
    - _Requirements: 8.7_
  - [x] 9.2 Implement `IAuthService` / `AuthService`
    - Validate credentials using bcrypt comparison; increment `FailedLoginAttempts` on failure; lock account for 15 minutes after 5 consecutive failures; set `LockedUntil` timestamp
    - On success: reset counter, issue JWT (HS256, 60-minute expiry, claims: `sub`, `username`, `iat`, `exp`, `jti`) signed with secret from `JwtSettings`
    - _Requirements: 8.1, 8.4, 8.5, 8.6, 8.7_
  - [x] 9.3 Implement `AuthController` with `POST /api/auth/login` and `POST /api/auth/logout`
    - Login: HTTP 200 + token on success; HTTP 401 on invalid credentials; HTTP 429 + lockout-until ISO timestamp on locked account
    - Response MUST NOT disclose whether username or password was incorrect
    - _Requirements: 8.3, 8.4, 8.5, 10.7_
  - [x] 9.4 Configure JWT bearer authentication middleware; apply `[Authorize]` to all admin endpoints
    - _Requirements: 8.1, 8.2_
  - [x] 9.5 Write property test for authentication lockout invariant
    - **Property 3: Admin Authentication Lockout Invariant**
    - **Validates: Requirements 8.5**
    - Generate sequences of 5–50 failed login attempts; assert all subsequent attempts (correct or incorrect credentials) within 15 minutes return locked response
  - [x] 9.6 Write unit tests for `AuthService`
    - Correct credential validation, lockout counter increment, lockout release after 15 minutes, JWT issuance with correct claims and expiry
    - _Requirements: 8.5, 8.6_

- [x] 10. Admin content management — backend
  - [x] 10.1 Implement update methods in `CompanyProfileRepository` for `CompanyContent`, `ServiceBlock`, and `LeadershipProfile` entities using EF Core transactions
    - All writes wrapped in transactions; roll back on any failure
    - _Requirements: 11.5, 9.2_
  - [x] 10.2 Extend `CompanyProfileService` with update methods for tagline, service block fields, and leadership profile biography; invalidate cache on successful write
    - Validate tagline ≤ 150 chars, service title ≤ 100 chars, description ≤ 500 chars, biography ≤ 2000 chars and not whitespace-only
    - _Requirements: 2.4, 3.6, 4.7, 4.8, 9.1, 9.2_
  - [x] 10.3 Implement `AdminController` with `PUT /api/admin/profile`, `PUT /api/admin/services/{id}`, `PUT /api/admin/leadership/{id}`, and `POST /api/admin/leadership/{id}/photo`
    - All endpoints require valid JWT; return HTTP 200 on success; HTTP 422 on validation error; HTTP 403 on missing/invalid token
    - _Requirements: 9.2, 9.8_
  - [x] 10.4 Write property test for biography length validation
    - **Property 4: Biography Length Validation**
    - **Validates: Requirements 4.7, 4.8**
    - Generate strings > 2000 chars and whitespace-only strings; assert API returns error and DB biography is unchanged
  - [x] 10.5 Write property test for admin content update reflection
    - **Property 8: Admin Content Update Reflected in Subsequent Reads**
    - **Validates: Requirements 2.4, 3.6, 9.2**
    - Generate arbitrary valid content strings; assert GET endpoint after PUT returns the updated value

- [x] 11. Checkpoint — backend complete
  - Ensure all backend unit and property tests pass. Verify Docker Compose stack starts with `docker compose up` and the health check at `http://localhost:5000/health` returns HTTP 200. Ask the user if any questions arise before proceeding to the frontend.

- [x] 12. Bootstrap React frontend project
  - [x] 12.1 Initialise React project with TypeScript using Create React App or Vite; install dependencies: `react-router-dom`, `fast-check`, `vitest`, `@testing-library/react`, `@testing-library/user-event`
    - _Requirements: 1.1, 12.1_
  - [x] 12.2 Create `apiClient.ts` module wrapping `fetch` with: base URL from `REACT_APP_API_URL`, `Authorization` header injection, 30-second `AbortController` timeout, centralised HTTP status → user-facing message mapping
    - _Requirements: 5.10, 10.2_
  - [x] 12.3 Implement React Context + `useReducer` global state for auth token and content cache; implement `ProtectedRoute` HOC that reads JWT from localStorage and redirects to `/admin/login` if absent or expired
    - _Requirements: 8.1, 8.2_
  - [x] 12.4 Write unit tests for `ProtectedRoute`
    - Redirects to `/admin/login` when no token present; renders children when valid token present
    - _Requirements: 8.2_

- [ ] 13. React routing, layout, and navigation
  - [-] 13.1 Configure React Router with all routes defined in the design: `/`, `/services`, `/about`, `/surveys`, `/surveys/customer`, `/surveys/employee`, `/contact`, `/admin/login`, `/admin`, and `*` (404)
    - All routes lazy-loaded; `*` renders `NotFoundPage` with link back to Home
    - _Requirements: 1.1, 1.2, 1.5_
  - [x] 13.2 Implement persistent `Navbar` component
    - Desktop: full navigation links (Home, Services, About Us, Surveys, Contact)
    - Mobile (< 768px): hamburger toggle with collapsible drawer; toggle expands/collapses on activation
    - _Requirements: 1.1, 1.6_
  - [x] 13.3 Implement `Footer` component with company name, copyright notice, and at least one social/contact link; render at 320px–2560px without overflow
    - _Requirements: 1.3, 1.4_
  - [x] 13.4 Write unit tests for `Navbar`
    - Collapsible menu visible below 768px; full bar visible at ≥ 768px; toggle expands and collapses
    - _Requirements: 1.6_

- [x] 14. Home page
  - [x] 14.1 Implement `HeroSection` component displaying "NNM Advisory", tagline (≤ 20 words), and "Contact Us" CTA button linking to `/contact`; display brand logo at ≥ 48px height with text fallback on `onError`
    - Tagline and CTA label fetched from `GET /api/profile`
    - _Requirements: 2.1, 2.3, 2.5_
  - [x] 14.2 Implement `HomePage` rendering `HeroSection` plus service area summaries (2–6 areas, title ≤ 10 words, description ≤ 40 words) fetched from `GET /api/profile/services`
    - Cache last successful response in localStorage; render from cache when API is unavailable
    - _Requirements: 2.2, 3.8_

- [x] 15. Services page
  - [x] 15.1 Implement `ServiceBlock` component and `ServicesPage`
    - Render four Service Blocks: Accounting Services (bookkeeping, tax services, financial analysis and insights), IT Services (financial system setup for payroll and HR, website development, IT infrastructure), Customer Surveys, Employee Surveys — each with title and description
    - Fetch from `GET /api/profile/services`; fall back to localStorage cache if API unavailable (never show empty blocks)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.8_

- [x] 16. About Us page and leadership profiles
  - [x] 16.1 Implement `LeadershipCard` component displaying photo (pre-signed URL from API), full name, title, and biography; photo minimum 200×200px, maximum 800×800px; fallback on missing photo
    - _Requirements: 4.4_
  - [x] 16.2 Implement `AboutPage` fetching `GET /api/profile/leadership` and rendering `LeadershipCard` for each profile in `display_order` (Ntwanano Mabasa COO first, Ntshovelo Priscilla Novela CIO second)
    - Seed database with both leadership profiles, their biographical data as specified in Requirement 4.2–4.3, and profile photos (from workspace JFIF/JPG files converted to JPEG)
    - _Requirements: 4.1, 4.2, 4.3_

- [x] 17. Contact page and form
  - [x] 17.1 Implement `ContactForm` component: controlled fields (Full Name, Email, Subject, Message), inline client-side validation (required, length limits, email format), `useForm` hook wrapping `apiClient`
    - On submit: show loading state; on HTTP 202: replace form with `SuccessMessage`; on HTTP 4xx/5xx: show error banner without clearing fields; on 30-second timeout: show `TimeoutMessage` without clearing fields
    - _Requirements: 5.1, 5.6, 5.7, 5.8, 5.9, 5.10_
  - [x] 17.2 Implement `ContactPage` rendering `ContactForm` in the Contact section
    - _Requirements: 5.1_
  - [x] 17.3 Write unit tests for `ContactForm`
    - Inline errors on empty fields; form not submitted on invalid email; confirmation message shown on success; field values preserved on server error
    - _Requirements: 5.6, 5.7, 5.8, 5.9_

- [x] 18. Survey pages — frontend
  - [x] 18.1 Implement shared `SurveyForm` base component supporting rating scale inputs and open-ended text fields; highlight unanswered required questions with inline errors on attempted submit without calling API
    - _Requirements: 6.4, 7.4, 7.8_
  - [x] 18.2 Implement `CustomerSurveyPage` using `SurveyForm`: service satisfaction (1–5), NPS score (0–10), open-ended text (≤ 1000 chars); on success display thank-you message within 3 seconds; retain answers on API error
    - Page load ≤ 3 seconds; accessible from Surveys navigation
    - _Requirements: 6.1, 6.2, 6.5, 6.6_
  - [x] 18.3 Implement `EmployeeSurveyPage` using `SurveyForm`: employee engagement, workplace satisfaction, open-ended feedback; no PII fields; required questions marked before submit attempt; thank-you message visible ≥ 5 seconds; retain answers on API error
    - Accessible from Surveys navigation
    - _Requirements: 7.1, 7.2, 7.5, 7.6, 7.7, 7.8_
  - [x] 18.4 Implement `SurveysPage` as landing page under `/surveys` with links to customer and employee survey routes
    - _Requirements: 6.1, 7.1_
  - [x] 18.5 Write property test for survey form client-side validation
    - **Property 6 (client-side): Survey Incomplete Submission**
    - **Validates: Requirements 6.4, 7.4**
    - Generate arbitrary sets of missing required fields; assert form does not call API and inline errors are displayed for each missing field (fast-check)

- [x] 19. Admin login page
  - [x] 19.1 Implement `AdminLoginPage` with username/password form; POST to `POST /api/auth/login`
    - On success: store JWT in localStorage, redirect to `/admin`; on failure: display error message (no field-level disclosure); on lockout (HTTP 429): display locked message with unlock time; on API unavailable: display "service temporarily unavailable"
    - Redirect unauthenticated access to `/admin` → `/admin/login` within 2 seconds
    - _Requirements: 8.2, 8.3, 8.4, 8.5, 8.8_

- [x] 20. Admin dashboard and content editor
  - [x] 20.1 Implement `AdminDashboardPage` (protected by `ProtectedRoute`) listing editable content sections: tagline, service blocks, leadership profiles
    - _Requirements: 9.1_
  - [x] 20.2 Implement `AdminContentEditor` component with form fields for tagline (≤ 150 chars), service block title (≤ 100 chars) and description (≤ 500 chars), and biography (≤ 2000 chars)
    - Live preview panel updates within 1 second of input cease; biography character counter turns red and save button disabled when > 2000 chars
    - On save: PUT to appropriate admin endpoint; display confirmation within 3 seconds (visible ≥ 3 seconds); on failure: display error without changing stored content
    - _Requirements: 9.1, 9.6, 9.7, 9.8_
  - [x] 20.3 Implement `ImageUploadField` component: client-side JPEG/PNG type check, ≤ 5 MB size check, image preview before confirm; POST to `POST /api/admin/leadership/{id}/photo` on confirm
    - On invalid file: display error message with accepted formats/size; on success: refresh leadership profile display
    - _Requirements: 9.3, 9.4, 9.5, 9.9_
  - [x] 20.4 Implement inactivity timer in admin context: reset on any API activity; after 60 minutes of inactivity clear JWT from localStorage and redirect to `/admin/login`
    - _Requirements: 8.6_
  - [x] 20.5 Write unit tests for `AdminContentEditor`
    - Live preview updates within 1 second of input change; biography counter turns red > 2000 chars; save button disabled > 2000 chars
    - _Requirements: 9.6_
  - [x] 20.6 Write property test for admin content update reflection (frontend)
    - **Property 8: Admin Content Update Reflected in Subsequent Reads (frontend)**
    - **Validates: Requirements 2.4, 3.6, 9.2**
    - Generate arbitrary valid content strings; assert GET after PUT returns updated value (fast-check, with mocked API)

- [x] 21. Checkpoint — frontend complete
  - Ensure all React unit and property tests pass (`vitest --run`). Verify Docker Compose stack is running and all public pages load correctly, the admin login flow works, and contact/survey forms submit successfully. Ask the user if any questions arise before proceeding to infrastructure.

- [x] 22. SQL injection safety — backend property test
  - [x] 22.1 Write property test for SQL injection safety
    - **Property 10: SQL Injection Safety**
    - **Validates: Requirements 11.3**
    - Generate strings containing SQL metacharacters (`'`, `--`, `;`, `DROP TABLE`); submit as contact and survey field values; assert fields are stored as literal strings, no SQL error raised, and DB integrity is intact (CsCheck)

- [x] 23. CORS policy — backend property test
  - [x] 23.1 Write property test for CORS rejection
    - **Property 9: CORS Policy Rejects Non-Configured Origins**
    - **Validates: Requirements 10.5**
    - Generate random `Origin` header strings that differ from the configured frontend origin; assert API returns HTTP 403 and does not process the request body (CsCheck)

- [x] 24. AWS infrastructure — IaC
  - [x] 24.1 Create VPC with public subnets (10.0.1.0/24, 10.0.2.0/24) and private subnets (10.0.11.0/24, 10.0.12.0/24) in two AZs; configure NAT Gateway in public subnet
    - _Requirements: 13.1_
  - [x] 24.2 Create security groups `sg-alb`, `sg-ecs`, `sg-rds` with rules as specified in the design (ALB: inbound 443 from 0.0.0.0/0; ECS: inbound 8080 from sg-alb only; RDS: inbound 5432 from sg-ecs only)
    - _Requirements: 13.1, 13.5_
  - [x] 24.3 Provision RDS PostgreSQL 14+ instance (`db.t3.medium`, gp3 storage, auto-scaling) in private subnets; Multi-AZ optional; no public accessibility
    - _Requirements: 13.5_
  - [x] 24.4 Create S3 bucket for static frontend assets with OAC-only access (no direct public access); create S3 bucket for leadership profile images (no public access)
    - _Requirements: 13.2_
  - [x] 24.5 Create ACM certificates for `nnmadvisory.com`, `www.nnmadvisory.com`, and `api.nnmadvisory.com`
    - _Requirements: 13.2, 13.3_
  - [x] 24.6 Create CloudFront distribution with S3 static bucket as origin (OAC), ACM certificate, HTTP→HTTPS redirect viewer protocol policy, and custom error responses (403/404 → `index.html` HTTP 200)
    - _Requirements: 13.2, 13.3_
  - [x] 24.7 Create ALB in public subnets with HTTPS:443 listener (ACM certificate for `api.nnmadvisory.com`) forwarding to ECS target group on port 8080; no HTTP:80 listener
    - _Requirements: 13.4, 13.7_
  - [x] 24.8 Create ECR repository; write multi-stage Dockerfile (`sdk` build stage → `aspnet` runtime stage) for the .NET API; configure ECS task definition and Fargate service (min 1 task) with `awslogs` log driver to CloudWatch log group `/nnm/api`
    - _Requirements: 13.4_
  - [x] 24.9 Store all secrets in AWS Systems Manager Parameter Store as `SecureString`: `/nnm/db/connection-string`, `/nnm/jwt/secret`, `/nnm/ses/sender-email`, `/nnm/ses/admin-email`, `/nnm/s3/images-bucket`, `/nnm/cors/allowed-origin`; configure ECS task role `nnm-api-task-role` with least-privilege IAM policies for SSM, SES, and S3 as specified in the design
    - _Requirements: 13.6_
  - [x] 24.10 Create Route 53 A alias records: `nnmadvisory.com` → CloudFront, `www.nnmadvisory.com` → CloudFront, `api.nnmadvisory.com` → ALB
    - _Requirements: 13.1_
  - [x] 24.11 Configure CloudWatch alarms: ALB 5xx rate > 1% over 5 min → SNS; ECS CPU > 80% over 5 min → auto-scaling trigger; RDS free storage < 20% → SNS
    - _Requirements: 13.1_
  - [x] 24.12 Configure `AWS.Extensions.Configuration.SystemsManager` in the .NET API to load Parameter Store values at startup using path `/nnm/`; configure ECS deployment pipeline step that runs EF Core migrations as a one-off task before promoting new API version
    - _Requirements: 13.6, 11.4_

- [x] 25. AWS deployment runbook document
  - [x] 25.1 Create `docs/aws-runbook.md` with the following distinct sections:
    - **Initial Deployment**: step-by-step for provisioning S3, CloudFront, ECS, RDS, ACM — each step identifies the service, the action, and the expected observable outcome
    - **Environment Variables and Secrets**: which variables are required, which AWS service stores each, and the exact configuration sequence before launch
    - **Database Migrations**: command to execute, expected success output, and verification step to confirm schema version
    - **Update Deployment**: steps to build/push new container image, update ECS task definition, and trigger rolling deployment
    - **Rollback Procedure**: trigger condition for initiating rollback, sequential steps to restore previous task definition version, and verification step confirming previous version is serving traffic
    - **Troubleshooting** (≥ 5 scenarios): symptom, sequential diagnostic steps, and remediation action for each
    - All commands include exact syntax, expected success output, and expected failure output
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7_

- [x] 26. Requirements specification Word document
  - [x] 26.1 Generate `docs/NNM Advisory - Requirements Specification.docx` from `requirements.md` containing: cover page (project name "NNM Advisory Website", document type "Requirements Specification", creation date in DD Month YYYY format), table of contents listing all top-level and second-level headings with page numbers, and all sections (Introduction, Glossary, all 15 Requirements with Acceptance Criteria) with heading hierarchy matching requirements.md
    - _Requirements: 15.1, 15.2, 15.3, 15.5_

- [x] 27. Final checkpoint — wire everything together and verify
  - Ensure all tests pass (backend xUnit/CsCheck, frontend Vitest/fast-check). Run `docker compose up` and verify: all public pages load, contact form submits, surveys submit, admin login and content editing work end-to-end. Confirm health check returns HTTP 200. Ask the user if any questions arise before closing out.

---

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP; they cover property-based tests and unit tests.
- Each task references specific requirements for full traceability back to requirements.md.
- The design's 11 correctness properties (Properties 1–11) are each covered by a dedicated property test sub-task annotated with the property number and the requirements clause it validates.
- CsCheck is used for all .NET backend property tests; fast-check is used for React frontend property tests (minimum 100 iterations per execution per design specification).
- Checkpoints at Tasks 11, 21, and 27 provide incremental validation gates before moving to the next major phase.
- The Word document task (26) satisfies Requirement 15; the existing `NNM Advisory - Requirements Specification.docx` in the workspace root may serve as the starting artefact.

---

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["2.1", "12.1"] },
    { "id": 1, "tasks": ["2.2", "2.3", "2.4", "12.2", "12.3"] },
    { "id": 2, "tasks": ["3.1", "12.4"] },
    { "id": 3, "tasks": ["3.2"] },
    { "id": 4, "tasks": ["3.3", "4.1", "7.1", "8.1", "9.1"] },
    { "id": 5, "tasks": ["4.2", "5.1", "6.1", "7.2", "7.3", "8.2", "8.3", "9.2", "13.1"] },
    { "id": 6, "tasks": ["4.3", "5.2", "6.2", "7.4", "8.4", "9.3", "10.1", "13.2", "13.3"] },
    { "id": 7, "tasks": ["4.4", "5.3", "6.3", "9.4", "10.2", "13.4", "14.1"] },
    { "id": 8, "tasks": ["5.4", "5.5", "6.4", "6.5", "6.6", "9.5", "9.6", "10.3", "14.2"] },
    { "id": 9, "tasks": ["10.4", "10.5", "15.1", "16.1", "17.1", "18.1", "19.1", "22.1", "23.1"] },
    { "id": 10, "tasks": ["15.2", "16.2", "17.2", "18.2", "18.3", "18.4", "20.1"] },
    { "id": 11, "tasks": ["17.3", "18.5", "20.2", "20.3", "20.4"] },
    { "id": 12, "tasks": ["20.5", "20.6", "24.1"] },
    { "id": 13, "tasks": ["24.2"] },
    { "id": 14, "tasks": ["24.3", "24.4", "24.5"] },
    { "id": 15, "tasks": ["24.6", "24.7"] },
    { "id": 16, "tasks": ["24.8", "24.9"] },
    { "id": 17, "tasks": ["24.10", "24.11"] },
    { "id": 18, "tasks": ["24.12", "25.1"] },
    { "id": 19, "tasks": ["26.1"] }
  ]
}
```
