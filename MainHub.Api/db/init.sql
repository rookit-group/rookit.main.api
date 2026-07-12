-- MainHub PostgreSQL schema
-- Executed once on first container start via /docker-entrypoint-initdb.d.
--
-- Coming from Mongo: there is no "collection" concept here. Every table has an
-- explicit, fixed set of typed columns (unlike a Mongo collection, which is
-- schema-less and just stores whatever BSON shape you insert). Relationships
-- that used to be embedded documents or ID arrays on the parent document are
-- now separate tables linked by foreign keys (FK) instead.

-- Mongo equivalent: the "Users" collection. One row per user, no VehicleIds
-- array here anymore - ownership is tracked from the vehicles side instead
-- (see vehicles.user_id below), the same way a relational FK usually points
-- from the "many" side to the "one" side.
CREATE TABLE IF NOT EXISTS users (
    id            uuid        PRIMARY KEY,
    name          text        NOT NULL,
    email         text        NULL,
    provider_id   text        NOT NULL UNIQUE,
    phone         text        NULL,
    picture_url   text        NULL,
    created_at    timestamptz NOT NULL,
    updated_at    timestamptz NULL
);

-- Mongo equivalent: the "Vehicles" collection.
-- user_id is a foreign key (FK) back to users.id - this replaces the old
-- UserEntity.VehicleIds array. "ON DELETE SET NULL" means if a user row is
-- deleted, Postgres automatically nulls out user_id here instead of throwing
-- or requiring you to clean it up in application code first.
-- wheel_drive_type / fuel_type / transmission_type are C# enums stored as
-- plain text (e.g. "Awd", "Petrol") rather than a Mongo BsonRepresentation -
-- there's no native "enum" column type used here, just text + Enum.Parse in code.
CREATE TABLE IF NOT EXISTS vehicles (
    id                uuid        PRIMARY KEY,
    user_id           uuid        NULL REFERENCES users(id) ON DELETE SET NULL,
    license_plate     text        NOT NULL,
    vin               text        NOT NULL,
    brand             text        NOT NULL,
    model             text        NOT NULL,
    year_created      int         NOT NULL,
    bought_at         timestamptz NULL,
    wheel_drive_type  text        NOT NULL,
    engine_capacity   int         NOT NULL,
    fuel_type         text        NOT NULL,
    transmission_type text        NOT NULL,
    engine_power      int         NOT NULL,
    color             text        NOT NULL,
    mileage           int         NOT NULL,
    photo_url         text        NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NULL
);

-- Postgres doesn't auto-index FK columns like Mongo would with a manual index
-- on a referenced field - we add it explicitly so "WHERE user_id = ..." (i.e.
-- "get all vehicles for this user") is fast instead of a full table scan.
CREATE INDEX IF NOT EXISTS ix_vehicles_user_id ON vehicles(user_id);

-- Mongo equivalent: the "ServiceHistories" collection. Previously each
-- document embedded its own "Records" array inline (a classic Mongo
-- embedded-document pattern). In Postgres that array becomes its own table
-- (service_history_records, below) joined back via vehicle_id/service_history_id
-- foreign keys instead of being nested in the same row.
-- "ON DELETE CASCADE" means deleting a vehicle automatically deletes all of
-- its service_histories rows too - no need for the app to loop and delete
-- children first like you would with Mongo document deletes.
CREATE TABLE IF NOT EXISTS service_histories (
    id          uuid        PRIMARY KEY,
    vehicle_id  uuid        NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    title       text        NOT NULL,
    description text        NOT NULL,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_service_histories_vehicle_id ON service_histories(vehicle_id);

-- This is the former "Records" embedded array, now its own table. Each row is
-- one line item that used to live inside a ServiceHistoryEntity document.
-- CASCADE here means deleting a service_histories row deletes its records too.
CREATE TABLE IF NOT EXISTS service_history_records (
    id                 uuid PRIMARY KEY,
    service_history_id uuid NOT NULL REFERENCES service_histories(id) ON DELETE CASCADE,
    title              text NOT NULL,
    description        text NOT NULL,
    price              int  NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_service_history_records_service_history_id
    ON service_history_records(service_history_id);

-- Mongo equivalent: the "RefreshTokens" collection - unchanged in shape,
-- just a plain table with a FK to users instead of a loose UserId field.
CREATE TABLE IF NOT EXISTS refresh_tokens (
    id          uuid        PRIMARY KEY,
    token       text        NOT NULL UNIQUE,
    user_id     uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider_id text        NOT NULL,
    expires_at  timestamptz NOT NULL,
    created_at  timestamptz NOT NULL,
    is_revoked  boolean     NOT NULL DEFAULT false
);

CREATE INDEX IF NOT EXISTS ix_refresh_tokens_expires_at ON refresh_tokens(expires_at);
