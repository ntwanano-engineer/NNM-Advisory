# Requirements Document

## Introduction

NNM Advisory is a professional services firm offering Accounting and IT services to businesses. This specification covers the development of a public-facing company profile website and its supporting backend infrastructure. The website serves as the primary digital presence for NNM Advisory, presenting company services, leadership profiles, and a contact channel for prospective clients. It also provides an admin interface for managing configurable company content. The system is built with a ReactJS frontend, a .NET backend API, and a PostgreSQL database, deployable both locally and on AWS.

---

## Glossary

- **Website**: The public-facing NNM Advisory company profile website.
- **Visitor**: An unauthenticated member of the public browsing the Website.
- **Client**: A Visitor who submits a contact request via the Contact Form.
- **Admin**: An authenticated NNM Advisory staff member with access to the Admin Page.
- **Admin Page**: A protected web interface used by Admins to manage configurable company content.
- **Contact Form**: The web form that allows a Client to send a message to NNM Advisory.
- **Acknowledgement Email**: An automated email sent to a Client confirming receipt of their contact message.
- **Admin Notification Email**: An email sent to the company admin inbox when a new contact message is received.
- **Company Admin Email**: The designated NNM Advisory email address that receives Client contact messages.
- **Leadership Profile**: A content block displaying a team member's name, title, biography, and profile photo.
- **Service Block**: A content block describing one of NNM Advisory's service offerings.
- **Survey**: An online questionnaire embedded in the Website targeting either Clients or Employees.
- **Frontend**: The ReactJS single-page application served to Visitors.
- **Backend API**: The .NET REST API that serves data to the Frontend and processes form submissions.
- **Database**: The PostgreSQL database that persists company profile content and contact submissions.
- **Runbook**: A step-by-step operational guide for deploying and maintaining the system on AWS.

---

## Requirements

---

### Requirement 1: Public Website Navigation and Layout

**User Story:** As a Visitor, I want to navigate a well-structured company website, so that I can quickly find information about NNM Advisory's services, leadership, and contact details.

#### Acceptance Criteria

1. THE Website SHALL display a persistent navigation bar containing links to the following sections: Home, Services, About Us, Surveys, and Contact.
2. WHEN a Visitor clicks a navigation link, THE Website SHALL scroll to or navigate to the corresponding page section without a full page reload within 1 second.
3. THE Website SHALL display a footer containing the company name, copyright notice, and at least one social or contact link.
4. THE Website SHALL be responsive and render correctly on desktop, tablet, and mobile screen sizes with a minimum supported viewport width of 320px and a maximum supported viewport width of 2560px, with no horizontal overflow or overlapping elements at any viewport width within this range.
5. IF a Visitor navigates to a URL path that does not exist, THEN THE Website SHALL display a 404 Not Found page with a link back to the Home page.
6. WHEN the viewport width is below 768px, THE Website SHALL replace the full navigation bar with a collapsible menu that expands and collapses when a Visitor activates the menu toggle.

---

### Requirement 2: Home Page

**User Story:** As a Visitor, I want to see an engaging home page, so that I can understand what NNM Advisory does at a glance.

#### Acceptance Criteria

1. THE Website SHALL display a hero section on the Home page containing the company name "NNM Advisory", a tagline of no more than 20 words, and a call-to-action button labelled "Contact Us" linking to the Contact section.
2. THE Website SHALL display a summary of NNM Advisory's core service areas on the Home page, listing between 2 and 6 service areas each with a title of no more than 10 words and a description of no more than 40 words.
3. THE Website SHALL display the NNM Advisory brand logo in the hero section of the Home page at a minimum height of 48px.
4. WHEN the Admin submits updated tagline or hero content via the Admin Page, THE Website SHALL reflect the updated content within 5 seconds for all subsequent Visitor page loads.
5. IF the NNM Advisory brand logo asset is unavailable, THEN THE Website SHALL display the text "NNM Advisory" as a fallback in place of the logo image.

---

### Requirement 3: Services Page

**User Story:** As a Visitor, I want to view detailed descriptions of NNM Advisory's services, so that I can assess which services are relevant to my business needs.

#### Acceptance Criteria

1. THE Website SHALL display a dedicated Services section containing one Service Block for each of the following service categories: Accounting Services, IT Services, Customer Surveys, and Employee Surveys, where each Service Block includes at minimum a title and a description.
2. THE Accounting Services block SHALL list the following offerings as distinct items: bookkeeping, tax services, and financial analysis and insights.
3. THE IT Services block SHALL list the following offerings as distinct items: financial system setup for payroll and HR, website development, and IT infrastructure.
4. THE Customer Surveys block SHALL describe NNM Advisory's capability to design and administer customer surveys for product and market insights, and SHALL include at minimum one sentence of descriptive content.
5. THE Employee Surveys block SHALL describe NNM Advisory's capability to design and administer employee engagement surveys for organisational insights, and SHALL include at minimum one sentence of descriptive content.
6. WHEN an Admin submits an update to a Service Block's title or description via the Admin Page, THE Backend API SHALL persist the change to the Database within 5 seconds and THE Website SHALL display the updated content on all subsequent page loads.
7. IF the Backend API fails to persist an Admin's update to a Service Block, THEN THE Backend API SHALL return an error response indicating the save was unsuccessful and THE Admin Page SHALL display an error message indicating that the update was not saved, leaving the previously saved content unchanged in the Database.
8. IF a Visitor loads the Services section while the Backend API is unavailable, THEN THE Website SHALL display the last successfully persisted Service Block content retrieved from a local cache or static fallback, and SHALL NOT display empty or missing Service Blocks.

---

### Requirement 4: About Us / Leadership Profiles

**User Story:** As a Visitor, I want to read about the NNM Advisory leadership team, so that I can understand the expertise and background of the people behind the company.

#### Acceptance Criteria

1. THE Website SHALL display an About Us section containing a Leadership Profile for each company leader, with profiles ordered with Ntwanano Mabasa (COO) listed first and Ntshovelo Priscilla Novela (CIO) listed second.
2. THE Website SHALL display a Leadership Profile for Ntshovelo Priscilla Novela with the title "Chief Information Officer (CIO)" and the following biographical details:
   - Qualified Chartered Accountant CA(SA)
   - Passed SAICA APC (December 2024) and ITC (June 2023)
   - Post Graduate Diploma in Accountancy CTA – UNISA (2022)
   - Bachelor of Accounting Sciences – WITS University (2018–2020)
   - Former Audit Senior at Deloitte (February 2022 – March 2026), managing audits for JSE- and NYSE-listed entities including Powerfleet, Africa Data Centers, Merafe, Tiger Brands, Motus, AECI, and Eskom
   - Current Senior Accountant at Tourvest Travel Services (April 2026 – present)
   - Areas of expertise: IFRS reporting, balance sheet substantiation, financial controls, management reporting, risk assessment, and financial analysis
3. THE Website SHALL display a Leadership Profile for Ntwanano Mabasa with the title "Chief Operating Officer (COO)".
4. EACH Leadership Profile SHALL display a profile photograph no smaller than 200×200 pixels and no larger than 800×800 pixels, a full name of no more than 100 characters, a title of no more than 100 characters, and a biography text of no more than 2000 characters.
5. WHEN an Admin uploads a new profile photograph via the Admin Page, THE Backend API SHALL reject files that are not JPEG or PNG format or exceed 5 MB in size with an error message indicating the reason, and SHALL store a valid image such that THE Website displays the new photograph for all subsequent Visitor page loads within 5 seconds of upload completion.
6. IF an Admin uploads a profile photograph that is not JPEG or PNG format or exceeds 5 MB in size, THEN THE Backend API SHALL return an error indicating the invalid file and THE Website SHALL retain the previously displayed photograph unchanged.
7. WHEN an Admin updates a leader's biography text via the Admin Page, THE Backend API SHALL reject input exceeding 2000 characters with an error message indicating the character limit, and SHALL persist valid input such that THE Website displays the updated biography for all subsequent Visitor page loads within 5 seconds of save completion.
8. IF an Admin submits a biography update that exceeds 2000 characters or contains only whitespace, THEN THE Backend API SHALL return an error indicating the validation failure and THE Website SHALL retain the previously displayed biography text unchanged.

---

### Requirement 5: Contact Page and Form Submission

**User Story:** As a Client, I want to send a message to NNM Advisory through the website, so that I can enquire about services or request a consultation.

#### Acceptance Criteria

1. THE Website SHALL display a Contact section containing a Contact Form with the following fields: Full Name (required, maximum 100 characters), Email Address (required, maximum 254 characters), Subject (required, maximum 150 characters), and Message (required, minimum 10 characters, maximum 2000 characters).
2. WHEN a Client submits the Contact Form with all required fields populated, THE Backend API SHALL store the submission in the Database within 5 seconds of receiving the request.
3. WHEN a Client submits the Contact Form with all required fields populated, THE Backend API SHALL send an Acknowledgement Email to the Client's provided email address within 60 seconds of submission.
4. THE Acknowledgement Email SHALL contain a personalised greeting with the Client's full name and confirm that NNM Advisory has received the message.
5. WHEN a Client submits the Contact Form with all required fields populated, THE Backend API SHALL send an Admin Notification Email to the Company Admin Email address containing the Client's full name, email, subject, and message body.
6. IF a Client submits the Contact Form with one or more required fields empty, THEN THE Website SHALL display an inline validation error adjacent to each empty field and SHALL NOT submit the form to the Backend API.
7. IF a Client provides an email address that does not conform to standard email format (local-part@domain.tld), THEN THE Website SHALL display an inline validation error on the Email Address field and SHALL NOT submit the form to the Backend API.
8. WHEN the Contact Form is successfully submitted, THE Website SHALL replace the Contact Form with a confirmation message indicating the message was sent successfully, within 3 seconds of receiving a success response from the Backend API.
9. IF the Backend API fails to process a Contact Form submission, THEN THE Backend API SHALL return an error response and THE Website SHALL display an error message instructing the Client to try again later, without clearing any fields already populated in the Contact Form.
10. IF the Backend API does not return a response to a Contact Form submission within 30 seconds, THEN THE Website SHALL display a timeout error message instructing the Client to try again later, without clearing any fields already populated in the Contact Form.

---

### Requirement 6: Customer Survey

**User Story:** As a Client, I want to complete a customer survey on the NNM Advisory website, so that I can provide feedback that helps improve NNM Advisory's services.

#### Acceptance Criteria

1. THE Website SHALL display a Customer Survey section accessible from the main navigation under "Surveys" with a page load time not exceeding 3 seconds.
2. THE Customer Survey SHALL contain at least the following question categories: service satisfaction rated on a scale of 1 to 5, likelihood to recommend rated on a scale of 0 to 10 (Net Promoter Score), and at least one open-ended text field accepting up to 1000 characters.
3. WHEN a Visitor submits a completed Customer Survey, THE Backend API SHALL store the survey responses in the Database with a UTC timestamp within 5 seconds of submission.
4. IF a Visitor attempts to submit a Customer Survey with one or more required questions unanswered, THEN THE Website SHALL highlight each unanswered required question with an inline error message indicating that a response is required and SHALL NOT submit the survey to the Backend API.
5. WHEN a Visitor successfully submits a Customer Survey, THE Website SHALL display a thank-you confirmation message within 3 seconds of the Backend API acknowledging the submission.
6. IF the Backend API fails to store a submitted Customer Survey within 5 seconds, THEN THE Website SHALL display an error message indicating that the submission was unsuccessful and SHALL retain the Visitor's entered responses so they can resubmit without re-entering their answers.

---

### Requirement 7: Employee Survey

**User Story:** As an Employee of an NNM Advisory client organisation, I want to complete an employee survey, so that I can provide confidential feedback about my workplace.

#### Acceptance Criteria

1. THE Website SHALL display an Employee Survey section accessible from the Surveys navigation item.
2. THE Employee Survey SHALL contain at least the following question categories: employee engagement, workplace satisfaction, and open-ended feedback, with a minimum of 1 question per category.
3. WHEN a respondent submits a completed Employee Survey, THE Backend API SHALL store the survey responses in the Database with a UTC timestamp within 5 seconds of submission.
4. IF a respondent attempts to submit an Employee Survey with required questions unanswered, THEN THE Website SHALL display validation errors indicating which questions require a response and SHALL NOT submit the survey.
5. WHEN a respondent successfully submits an Employee Survey, THE Website SHALL display a thank-you confirmation message that remains visible for a minimum of 5 seconds before any automatic navigation occurs.
6. THE Employee Survey submission SHALL NOT require the respondent to provide personally identifiable information, including but not limited to name, email address, and employee ID.
7. IF the Backend API fails to store a submitted Employee Survey, THEN THE Website SHALL display an error message indicating submission was unsuccessful and SHALL retain the respondent's answers so they may resubmit without re-entering responses.
8. WHEN a respondent begins an Employee Survey, THE Website SHALL indicate which questions are required before the respondent attempts to submit.

---

### Requirement 8: Admin Page – Authentication

**User Story:** As an Admin, I want to log in to a protected admin interface, so that only authorised users can modify company content.

#### Acceptance Criteria

1. THE Admin Page SHALL be accessible only to users who have successfully authenticated with a valid username and password, where the username is a non-empty string of at most 254 characters and the password is a non-empty string of at most 128 characters.
2. WHEN an unauthenticated user attempts to access the Admin Page URL, THE Website SHALL redirect the user to the Admin login page within 2 seconds.
3. WHEN an Admin submits correct credentials, THE Backend API SHALL return a session token and THE Website SHALL grant the Admin access to the Admin Page within 3 seconds of submission.
4. IF an Admin submits incorrect credentials, THEN THE Backend API SHALL return an authentication failure response and THE Website SHALL display an error message indicating the credentials are invalid, without specifying which field is incorrect.
5. IF an Admin submits incorrect credentials 5 or more consecutive times, THEN THE Backend API SHALL lock the account for 15 minutes and THE Website SHALL display a message indicating the account is temporarily locked and when it will be unlocked.
6. WHEN an authenticated Admin session exceeds 60 minutes of inactivity, THE Backend API SHALL invalidate the session token and THE Website SHALL redirect the Admin to the login page within 2 seconds of the token being invalidated.
7. THE Backend API SHALL store Admin passwords as salted cryptographic hashes and SHALL NOT store passwords in plaintext.
8. IF the Backend API is unavailable when an Admin submits credentials, THEN THE Website SHALL display an error message indicating the service is temporarily unavailable and SHALL NOT grant access to the Admin Page.

---

### Requirement 9: Admin Page – Content Management

**User Story:** As an Admin, I want to update the company profile content through the admin interface, so that the website always reflects accurate and current information.

#### Acceptance Criteria

1. THE Admin Page SHALL allow an authenticated Admin to update the following content items: company tagline (maximum 150 characters), service block titles (maximum 100 characters each) and descriptions (maximum 500 characters each), leadership profile biographies (maximum 1000 characters each), and leadership profile photographs.
2. WHEN an Admin saves a content update via the Admin Page, THE Backend API SHALL persist the change to the Database within 5 seconds and return a success response.
3. WHEN an Admin uploads a new leadership profile photograph, THE Backend API SHALL accept image files in JPEG and PNG formats with a maximum file size of 5 MB and minimum dimensions of 200×200 pixels.
4. IF an Admin uploads a file that is not a JPEG or PNG, THEN THE Backend API SHALL reject the upload and return an error response, and THE Admin Page SHALL display an error message indicating the accepted file formats (JPEG and PNG).
5. IF an Admin uploads an image file exceeding 5 MB, THEN THE Backend API SHALL reject the upload and return an error response, and THE Admin Page SHALL display an error message indicating the 5 MB maximum file size limit.
6. WHEN an Admin modifies a text content field, THE Admin Page SHALL display a live preview of the updated text within 1 second of the Admin ceasing input, reflecting the change exactly as it will appear on the public-facing website.
7. WHEN an Admin successfully saves a content update, THE Admin Page SHALL display a confirmation message within 3 seconds of the save operation completing, and the confirmation message SHALL remain visible for a minimum of 3 seconds before dismissing.
8. IF the Backend API fails to persist a content update within 5 seconds, THEN THE Admin Page SHALL display an error message indicating the save failed and the previously saved content SHALL remain unchanged in the Database.
9. WHEN an Admin uploads a new leadership profile photograph, THE Admin Page SHALL display a preview of the uploaded image before the Admin confirms the save.

---

### Requirement 10: Backend API

**User Story:** As a developer, I want a well-structured REST API, so that the frontend and third-party integrations can reliably consume company data and process form submissions.

#### Acceptance Criteria

1. THE Backend API SHALL expose RESTful endpoints for the following operations: retrieving company profile content, submitting contact form messages, submitting survey responses, authenticating Admin users, and updating company profile content.
2. WHEN the Backend API processes a request successfully, THE Backend API SHALL return a response in JSON format with an HTTP status code in the 2xx range within 3 seconds.
3. WHEN the Backend API receives a request with a malformed JSON body, THE Backend API SHALL return an HTTP 400 response with an error message indicating the location of the syntax error and reject the request without processing it.
4. WHEN the Backend API receives a request payload with missing required fields, THE Backend API SHALL return an HTTP 422 response with an error message identifying each missing field by name and reject the request without processing it.
5. THE Backend API SHALL implement CORS policies that restrict cross-origin requests to the configured Frontend origin, and reject requests from any other origin with an HTTP 403 response.
6. THE Backend API SHALL log all unhandled exceptions with a UTC timestamp, request path, and error message to a persistent log store.
7. WHEN the Backend API receives an Admin authentication request with invalid credentials, THE Backend API SHALL return an HTTP 401 response with an error message indicating authentication failure within 3 seconds without disclosing whether the username or password was incorrect.
8. IF the Backend API fails to connect to the persistent log store, THEN THE Backend API SHALL continue processing requests and attempt to write log entries to a fallback local log file.

---

### Requirement 11: Database

**User Story:** As a developer, I want a structured PostgreSQL database, so that all application data is persisted reliably and consistently.

#### Acceptance Criteria

1. THE Database SHALL persist the following data entities: company profile content, leadership profiles, contact form submissions, customer survey responses, employee survey responses, and Admin user credentials.
2. THE Database SHALL enforce referential integrity constraints between related tables, rejecting any insert, update, or delete operation that would violate a foreign key constraint and returning an error indicating the constraint violation to the caller.
3. THE Backend API SHALL use parameterised queries or an ORM when executing all Database operations to prevent SQL injection.
4. THE Database schema SHALL be managed via versioned migration scripts, where each script is uniquely numbered in ascending order, and applying all scripts in sequence to an empty PostgreSQL database produces the complete schema without errors.
5. IF a Database write operation fails due to a constraint violation, system error, or loss of connection, THEN THE Backend API SHALL roll back the entire transaction, leave all affected rows unchanged, and return an error response indicating the operation did not complete.
6. WHEN a contact form submission, customer survey response, or employee survey response is persisted, THE Database SHALL record a UTC timestamp of when the record was created.

---

### Requirement 12: Local Development Environment

**User Story:** As a developer, I want to run the full system locally, so that I can develop and test features without requiring a cloud environment.

#### Acceptance Criteria

1. THE system SHALL be runnable locally using a single command or a documented sequence of no more than five commands, where each command is listed in the project README with its expected output.
2. THE local environment SHALL run all three system components — Frontend, Backend API, and Database — concurrently, with each component's process output accessible in the developer's terminal session.
3. WHEN the local environment starts, THE system SHALL confirm all three components are operational by returning a successful health-check response from the Backend API within 60 seconds of executing the startup command.
4. THE local environment SHALL use environment variable configuration files for all configurable values including database connection strings, email credentials, and API URLs, and the project SHALL include a sample configuration file containing all required variable names with placeholder values and no real credentials.
5. THE local environment configuration files containing real credential values SHALL be excluded from version control via `.gitignore`.
6. IF a required environment variable is absent or empty at startup, THEN THE system SHALL halt startup and display an error message identifying each missing variable name before any component begins accepting connections.

---

### Requirement 13: AWS Deployment

**User Story:** As a developer and administrator, I want to deploy the NNM Advisory website to AWS, so that it is publicly accessible with high availability and scalability.

#### Acceptance Criteria

1. THE system SHALL be deployable to AWS using infrastructure configuration files that define components and their interdependencies, supporting the Frontend, Backend API, and Database components.
2. THE Frontend SHALL be deployable to AWS S3 with static website hosting and served exclusively via AWS CloudFront using an ACM certificate for HTTPS enforcement.
3. THE CloudFront distribution SHALL redirect all HTTP requests to HTTPS using a redirect rule configured on the distribution.
4. THE Backend API SHALL be deployable to AWS Elastic Beanstalk or AWS ECS and SHALL expose a health check endpoint that returns an HTTP 2xx response within 5 seconds of a request.
5. THE Database SHALL be provisioned using AWS RDS with a PostgreSQL engine version 14 or later, configured for VPC-only access and not publicly exposed to the internet.
6. THE deployment configuration SHALL store all credentials, API keys, and environment-specific configuration in AWS Systems Manager Parameter Store or AWS Secrets Manager, and SHALL NOT store these values in source code or build artifacts.
7. THE Backend API SHALL reject all plaintext HTTP connections and SHALL only accept HTTPS connections on the configured port.
8. WHEN the deployment is complete, THE Backend API health check endpoint SHALL return an HTTP 2xx response within 5 seconds when called from the Frontend's configured HTTPS endpoint.

---

### Requirement 14: AWS Deployment Runbook

**User Story:** As a developer or administrator, I want a detailed AWS deployment runbook, so that I can deploy, update, and troubleshoot the system on AWS without prior specialised knowledge of the infrastructure.

#### Acceptance Criteria

1. THE system SHALL include an AWS Deployment Runbook document as part of the project deliverables, covering initial deployment, update deployment, and rollback procedures as distinct sections.
2. THE Runbook SHALL contain step-by-step instructions for provisioning all required AWS services including S3, CloudFront, Elastic Beanstalk or ECS, RDS, and ACM, where each step identifies the AWS service being configured, the specific action to perform, and the expected observable outcome confirming the step succeeded.
3. THE Runbook SHALL include instructions for configuring environment variables and secrets in the AWS environment, specifying which variables are required for the application to start, which AWS service stores each value, and the exact sequence of configuration steps before the application is launched.
4. THE Runbook SHALL include instructions for applying database migration scripts against the AWS RDS instance, specifying the command to execute, the expected output indicating successful migration, and the verification step to confirm the schema matches the expected version before proceeding.
5. THE Runbook SHALL include a troubleshooting section covering no fewer than 5 common failure scenarios, where each scenario specifies the observable symptom, the diagnostic steps in sequential order, and the remediation action to resolve the issue.
6. THE Runbook SHALL include rollback instructions that specify the trigger condition indicating a failed deployment, the sequential steps to restore the previous known-good deployment, and a verification step confirming the system is serving the previous version successfully before the rollback procedure is considered complete.
7. WHEN a Runbook step requires executing a command, THE Runbook SHALL include the exact command syntax, the expected output on success, and the expected output on failure so that the operator can distinguish between a successful and failed execution without prior infrastructure knowledge.

---

### Requirement 15: Specification Word Document

**User Story:** As a project stakeholder, I want the full specification available as a Word document, so that it can be shared, reviewed, and approved using standard office productivity tools.

#### Acceptance Criteria

1. THE project SHALL include a Word document (.docx) that contains the complete requirements specification including the Introduction, Glossary, and all Requirements sections with their Acceptance Criteria, such that no section present in the requirements.md file is omitted from the Word document.
2. THE Word document SHALL use a heading hierarchy where each heading level in the Word document corresponds to the equivalent heading level in the requirements.md file, with no heading levels added, removed, or reordered.
3. THE Word document SHALL include a cover page containing the project name "NNM Advisory Website", the document type "Requirements Specification", and the creation date in DD Month YYYY format, each appearing as a distinct, labelled field on the cover page.
4. WHEN a new version of requirements.md is produced, THE Word document SHALL be updated to reflect all changes from that version before the next scheduled review milestone, with no section, criterion, or glossary entry present in requirements.md missing from the updated Word document.
5. WHEN the Word document is opened, THE Word document SHALL be navigable via a table of contents that lists all top-level and second-level headings with their corresponding page numbers.
