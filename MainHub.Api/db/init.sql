-- MainHub PostgreSQL schema
-- Executed once on first container start via /docker-entrypoint-initdb.d.

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

CREATE INDEX IF NOT EXISTS ix_vehicles_user_id ON vehicles(user_id);

CREATE TABLE IF NOT EXISTS service_histories (
    id          uuid        PRIMARY KEY,
    vehicle_id  uuid        NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    title       text        NOT NULL,
    description text        NOT NULL,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_service_histories_vehicle_id ON service_histories(vehicle_id);

CREATE TABLE IF NOT EXISTS service_history_records (
    id                 uuid PRIMARY KEY,
    service_history_id uuid NOT NULL REFERENCES service_histories(id) ON DELETE CASCADE,
    title              text NOT NULL,
    description        text NOT NULL,
    price              int  NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_service_history_records_service_history_id
    ON service_history_records(service_history_id);

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
