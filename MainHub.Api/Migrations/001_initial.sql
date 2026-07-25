-- MainHub PostgreSQL schema
-- Executed once on first container start via /docker-entrypoint-initdb.d.
--
-- KEY CONCEPTS:
-- - PRIMARY KEY: Unique identifier for each row
-- - FOREIGN KEY (FK): Link to another table
-- - ONE-TO-MANY: One user can own many vehicles
-- - MANY-TO-ONE: Many vehicles belong to one user
-- - ON DELETE CASCADE: If parent row is deleted, delete all child rows too
-- - ON DELETE SET NULL: If parent row is deleted, set FK to empty instead
-- - INDEX: Speeds up queries on specific columns
--

-- GARAGE TABLE: Stores garage information
CREATE TABLE IF NOT EXISTS garages (
    -- Unique identifier for this garage
    id          uuid        PRIMARY KEY,
    -- Name of the garage (required)
    name        text        NOT NULL,
    -- When this garage was created
    created_at  timestamptz NOT NULL,
    -- When this garage info was last updated
    updated_at  timestamptz NULL
);


-- USERS TABLE: Stores user account information
-- Relationship: ONE user has MANY vehicles (ONE-TO-MANY)
-- Relationship: ONE user has MANY refresh tokens (ONE-TO-MANY)
CREATE TABLE IF NOT EXISTS users (
    -- Unique identifier (uuid = universally unique identifier, a random 36-char ID)
    id            uuid        PRIMARY KEY,
    -- User's full name (text = string/text)
    name          text        NOT NULL,
    -- User's email address (NULL = optional field, can be empty)
    email         text        NULL,
    -- ID from the auth provider (Telegram, Google, etc) - UNIQUE = can't have duplicates
    provider_id   text        NOT NULL UNIQUE,
    -- User's phone number (optional)
    phone         text        NULL,
    -- URL to user's profile picture (optional)
    picture_url   text        NULL,
    -- When this user was created (timestamptz = date + time with timezone info)
    created_at    timestamptz NOT NULL,
    -- When this user was last updated (optional, NULL until first update)
    updated_at    timestamptz NULL
);

-- EXTERNAL USER PROFILES TABLE: Stores external user profile information
-- Relationship: ONE user has ONE external user profile (ONE-TO-ONE)
-- Relationship: ONE external user profile has MANY vehicles (ONE-TO-MANY)
CREATE TABLE IF NOT EXISTS external_user_profiles (
    id          uuid        PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NULL
);

-- INTERNAL USER PROFILES TABLE: Stores internal user profile information
-- Relationship: ONE user has ONE internal user profile (ONE-TO-ONE)
CREATE TABLE IF NOT EXISTS internal_user_profiles (
    id          uuid        PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NULL
);

-- ROLES TABLE: Owner-customizable, garage-scoped roles.
-- Roles are DATA (created/edited by garage owners in a UI), not a fixed code enum.
-- Application code is written against the fixed SCOPE vocabulary (see Authorization/Scope.cs);
-- a role is just a garage-defined bundle of those scopes.
CREATE TABLE IF NOT EXISTS roles (
    -- Unique identifier for this role
    id          uuid        PRIMARY KEY,
    -- Link to the garage this role is for (FK to garages table) - required, deletes role if garage deleted
    garage_id   uuid        NOT NULL REFERENCES garages(id) ON DELETE CASCADE,
    -- Name of the role, owner-defined (e.g. "Owner", "Senior Mechanic") (required)
    name        text        NOT NULL,
    -- Description of what this role can do (optional)
    description text        NULL,
    -- Scopes/permissions granted by this role (array of text, required). Each value is either
    -- a member of the code-defined scope catalog (see Authorization/Scope.cs), e.g.
    -- {"staff:manage","staff:read"}, or the wildcard '*' meaning "every scope, including ones
    -- added in the future". The Owner role seeded on garage creation holds exactly {'*'}, so it
    -- never needs re-seeding when new scopes are introduced.
    scopes      text[]      NOT NULL,
    -- Marks a system-provided role that the platform seeds and owns (currently just the per-garage
    -- "Owner" role). System roles are immutable: the service refuses to edit or delete them, so an
    -- owner can never accidentally strip the Owner role's scopes and lock the garage out. User-
    -- created roles have is_system = false and are fully editable/deletable.
    is_system   boolean     NOT NULL DEFAULT false,
    -- When this role was created
    created_at  timestamptz NOT NULL,
    -- When this role was last updated
    updated_at  timestamptz NULL,
    -- Role name must be unique within a garage
    UNIQUE (garage_id, name),
    -- Composite candidate key so the membership table can FK on (id, garage_id) and enforce
    -- that a role assignment cannot cross garage boundaries.
    UNIQUE (id, garage_id)
);

-- INDEX: Speeds up "find all roles for a garage" queries and FK cascade lookups
CREATE INDEX IF NOT EXISTS ix_roles_garage_id ON roles(garage_id);

-- INTERNAL_USER_PROFILES_GARAGES TABLE: Junction/link table for the MANY-TO-MANY relationship
-- Relationship: MANY internal_user_profiles belong to MANY garages
-- Each row = one internal_user_profile is linked to one garage
CREATE TABLE IF NOT EXISTS internal_user_profiles_garages (
    -- Link to the internal user profile (FK to internal_user_profiles table) - deletes link if internal user profile deleted
    internal_user_profile_id    uuid        NOT NULL REFERENCES internal_user_profiles(id)   ON DELETE CASCADE,
    -- Link to the garage (FK to garages table) - deletes link if garage deleted
    garage_id  uuid        NOT NULL REFERENCES garages(id) ON DELETE CASCADE,
    -- The internal user's single role within THIS garage. Role is per-membership because a
    -- user can belong to several garages and hold a different role in each. NOT NULL: every
    -- member always has exactly one role, so there is no scope-less "zombie" member state and
    -- permission checks never need a "no role" branch. The COMPOSITE FK (role_id, garage_id) ->
    -- roles(id, garage_id) guarantees the assigned role belongs to the SAME garage as the member.
    role_id    uuid        NOT NULL,
    -- When this user was added to this garage
    created_at timestamptz NOT NULL,
    -- When this user was last updated in this garage
    updated_at timestamptz NULL,
    -- COMPOSITE PRIMARY KEY: the pair (internal_user_profile_id, garage_id) must be unique
    -- This prevents linking the same internal user profile to the same garage twice
    PRIMARY KEY (internal_user_profile_id, garage_id),
    -- Composite FK: the assigned role must belong to the SAME garage as this membership.
    -- ON DELETE NO ACTION enforces "a role cannot be deleted while any member holds it" at the
    -- DB level (defence in depth behind the service-layer check). NO ACTION (not RESTRICT) is
    -- deferred to end-of-statement, so deleting a GARAGE still works: the membership rows are
    -- removed by their own garage-cascade first, leaving no dangling references when the garage's
    -- roles are cascade-deleted in the same statement.
    FOREIGN KEY (role_id, garage_id)
        REFERENCES roles(id, garage_id)
        ON DELETE NO ACTION
);

-- INDEX: Speeds up "find all users in a garage" queries
CREATE INDEX IF NOT EXISTS ix_internal_user_profiles_garages_garage_id ON internal_user_profiles_garages(garage_id);
-- INDEX: Speeds up "find all members with a given role" queries and FK cascade lookups
CREATE INDEX IF NOT EXISTS ix_internal_user_profiles_garages_role_id ON internal_user_profiles_garages(role_id);
-- (the composite PK already indexes internal_user_profile_id first, so "find all garages for an internal user profile" is covered)

-- VEHICLES TABLE: Stores vehicle/car information
-- Relationship: MANY vehicles belong to ONE external user profile (MANY-TO-ONE with external_user_profiles table)
-- Relationship: ONE vehicle has MANY service histories (ONE-TO-MANY)
CREATE TABLE IF NOT EXISTS vehicles (
    -- Unique identifier for this vehicle
    id                uuid        PRIMARY KEY,
    -- Link to the external user profile who owns this vehicle (FK to external_user_profiles table) - required, deletes vehicle if external user profile deleted
    external_user_profile_id         uuid        NOT NULL REFERENCES external_user_profiles(id) ON DELETE CASCADE,
    -- Vehicle license plate number (required)
    license_plate     text        NOT NULL,
    -- Vehicle Identification Number - unique identifier assigned by manufacturer (required)
    vin               text        NOT NULL,
    -- Make/brand of vehicle (e.g. "Toyota", "BMW") (required)
    brand             text        NOT NULL,
    -- Model name (e.g. "Camry", "3 Series") (required)
    model             text        NOT NULL,
    -- Year the vehicle was manufactured (int = integer/whole number) (required)
    year_created      int         NOT NULL,
    -- Date when this vehicle was purchased (optional)
    bought_at         timestamptz NULL,
    -- Wheel drive type (e.g. "Awd", "Fwd", "Rwd") - stored as text, not as enum
    wheel_drive_type  text        NOT NULL,
    -- Engine capacity in cubic centimeters (cc) (required)
    engine_capacity   int         NOT NULL,
    -- Fuel type (e.g. "Petrol", "Diesel", "Electric") - stored as text, not as enum
    fuel_type         text        NOT NULL,
    -- Transmission type (e.g. "Manual", "Automatic") - stored as text, not as enum
    transmission_type text        NOT NULL,
    -- Engine power in horsepower (hp) (required)
    engine_power      int         NOT NULL,
    -- Vehicle color (required)
    color             text        NOT NULL,
    -- Current mileage/odometer reading (int, required)
    mileage           int         NOT NULL,
    -- Array of photo storage keys (text[] = array of text) for photos stored in file system (optional)
    photo_storage_keys text[]     NULL,
    -- When this vehicle was added to system
    created_at        timestamptz NOT NULL,
    -- When this vehicle info was last updated
    updated_at        timestamptz NULL
);

-- INDEX: Speeds up queries that find all vehicles for a specific user
CREATE INDEX IF NOT EXISTS ix_vehicles_external_user_profile_id ON vehicles(external_user_profile_id);

-- SERVICE_HISTORIES TABLE: Stores vehicle service history/maintenance records
-- Relationship: MANY service histories belong to ONE vehicle (MANY-TO-ONE with vehicles table)
-- Relationship: ONE service history has MANY service records (ONE-TO-MANY)
CREATE TABLE IF NOT EXISTS service_histories (
    -- Unique identifier for this service history entry
    id          uuid        PRIMARY KEY,
    -- Link to the vehicle this service is for (FK to vehicles table) - required, deletes all records if vehicle deleted
    vehicle_id  uuid        NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    -- Title/name of the service (e.g. "Annual Maintenance", "Oil Change") (required)
    title       text        NOT NULL,
    -- Detailed description of what was serviced (required)
    description text        NOT NULL,
    -- When this service record was created
    created_at  timestamptz NOT NULL,
    -- When this service record was last updated
    updated_at  timestamptz NULL
);

-- INDEX: Speeds up queries that find all service histories for a specific vehicle
CREATE INDEX IF NOT EXISTS ix_service_histories_vehicle_id ON service_histories(vehicle_id);

-- SERVICE_HISTORY_RECORDS TABLE: Individual line items within a service history
-- Relationship: MANY records belong to ONE service history (MANY-TO-ONE with service_histories table)
-- Example: One service_history might have multiple records (new oil, new filter, labor cost, etc)
CREATE TABLE IF NOT EXISTS service_history_records (
    -- Unique identifier for this service record line item
    id                 uuid PRIMARY KEY,
    -- Link to the parent service history (FK to service_histories table) - required, deletes record if parent deleted
    service_history_id uuid NOT NULL REFERENCES service_histories(id) ON DELETE CASCADE,
    -- Name of the service item (e.g. "Oil Change", "Replace Air Filter", "Labor") (required)
    title              text NOT NULL,
    -- Details about this service item (required)
    description        text NOT NULL,
    -- Cost of this item in the smallest currency unit (e.g. cents if USD) (int, required)
    price              int  NOT NULL
);

-- INDEX: Speeds up queries that find all records for a specific service history
CREATE INDEX IF NOT EXISTS ix_service_history_records_service_history_id
    ON service_history_records(service_history_id);

-- REFRESH_TOKENS TABLE: Stores authentication tokens for users
-- Relationship: MANY tokens belong to ONE user (MANY-TO-ONE with users table)
CREATE TABLE IF NOT EXISTS refresh_tokens (
    -- Unique identifier for this token record
    id          uuid        PRIMARY KEY,
    -- The actual token string (long random text) - UNIQUE = no duplicates allowed (required)
    token       text        NOT NULL UNIQUE,
    -- Link to the user who owns this token (FK to users table) - required, deletes token if user deleted
    user_id     uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    -- Provider ID (e.g. Telegram user ID, Google ID) that issued this token (required)
    provider_id text        NOT NULL,
    -- When this token expires/becomes invalid (required)
    expires_at  timestamptz NOT NULL,
    -- When this token was created
    created_at  timestamptz NOT NULL,
    -- Whether this token has been revoked/invalidated (boolean = true/false) (default = false/not revoked)
    is_revoked  boolean     NOT NULL DEFAULT false
);

-- INDEX: Speeds up queries that find expired tokens for cleanup
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_expires_at ON refresh_tokens(expires_at);
