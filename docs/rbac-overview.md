# Per-Garage RBAC — Team Overview

Simple diagrams for presenting the internal-staff authorization model. All
diagrams are [Mermaid](https://mermaid.js.org/) and render in GitHub and VS Code.

---

## 1. The big idea: Scopes are code, Roles are data

We never check role names in code. Code checks **scopes** (a fixed vocabulary).
A **role** is just a garage-owned bundle of scopes that owners edit in the UI.

```mermaid
flowchart LR
    subgraph CODE["🔒 CODE - fixed vocabulary (Scope.cs)"]
        S1["garage:read / garage:manage"]
        S2["role:read / role:manage"]
        S3["staff:read / staff:manage"]
        S4["* (wildcard = everything)"]
    end

    subgraph DATA["🗂️ DATA - owner-defined per garage (roles table)"]
        R1["Owner (system)  →  *"]
        R2["Manager  →  staff:manage, role:read ..."]
        R3["Mechanic  →  staff:read, garage:read"]
    end

    CODE -- "owners assign scopes to roles" --> DATA
    DATA -- "endpoints check scopes, never role names" --> CODE
```

---

## 2. Two-stage token flow

Login gives an **identity** token (who you are). Opening a garage gives a
short-lived **garage** token (what you can do *in that garage*).

```mermaid
sequenceDiagram
    autonumber
    actor U as Staff user
    participant API
    participant DB

    Note over U,API: Stage 1 — Identity
    U->>API: Login (OAuth provider)
    API->>DB: get-or-create user + internal profile (EnsureAsync)
    API-->>U: InternalIdentityJwt (~60 min, no garage, no scopes)

    U->>API: GET /api/garages  (list my garages)
    API->>DB: memberships for this user
    API-->>U: [ garages I belong to ]

    Note over U,API: Stage 2 — Garage session
    U->>API: POST /api/garages/{id}/session
    API->>DB: resolve my scopes in this garage (ONCE)
    API-->>U: GarageJwt (~5 min, garage_id + scopes baked in)

    Note over U,API: Every garage action
    U->>API: e.g. GET /api/garages/{id}/staff  (+ GarageJwt)
    API-->>U: authorized by CLAIMS only — no DB lookup
```

**Why bake scopes into the token?** Authorization becomes a pure claim check —
no DB hit per request. The 5-minute lifetime bounds how long a role change can
be stale.

---

## 3. Request-time authorization pipeline

What `RequireScope(Scope.X)` actually does on every garage-scoped request — all
without touching the database.

```mermaid
flowchart TD
    A["Request + GarageJwt"] --> B{Valid GarageJwt?}
    B -- no --> R401["401 Unauthorized"]
    B -- yes --> C{"route {garageId} == token garage_id ?"}
    C -- no --> R403a["403 - wrong garage's token"]
    C -- yes --> D{"token scopes grant required scope?<br/>(wildcard counts)"}
    D -- no --> R403b["403 - missing scope"]
    D -- yes --> E["✅ Handler runs"]
```

> The pipeline verifies the **garage context** and the **scope** only. It does
> NOT check that a `{roleId}`/`{userId}` in the URL belongs to the garage — the
> **service layer** re-checks that.

---

## 4. Endpoint vs Service — separation of duties

```mermaid
flowchart LR
    subgraph EP["Endpoint (thin)"]
        E1["validate DTO shape<br/>(FluentValidation filter)"]
        E2["read actor scopes<br/>from token claim"]
        E3["map exceptions →<br/>404 / 403 / 409"]
    end

    subgraph SVC["Service (unbypassable rules)"]
        G1["Escalation guard<br/>can't grant scopes you lack"]
        G2["System-role immutable<br/>(Owner)"]
        G3["Last-staff-manager guard"]
        G4["Invitation phone match<br/>accept only your own"]
        G5["Cross-garage role isolation"]
    end

    subgraph DBG["Database (defence in depth)"]
        D1["composite FK (role_id, garage_id)"]
        D2["UNIQUE (garage_id, name)"]
        D3["role_id NOT NULL"]
    end

    EP --> SVC --> DBG
```

---

## 5. Data model

```mermaid
erDiagram
    users ||--o| internal_user_profiles : "has one"
    internal_user_profiles ||--o{ internal_user_profiles_garages : "member of"
    garages ||--o{ internal_user_profiles_garages : "has members"
    garages ||--o{ roles : "defines"
    roles ||--o{ internal_user_profiles_garages : "assigned to member"
    garages ||--o{ invitations : "has pending"
    roles ||--o{ invitations : "invited as"

    users {
        uuid id PK
        text name
        text phone "matched on invite accept"
        text provider_id UK
    }
    internal_user_profiles {
        uuid id PK
        uuid user_id UK "one profile per user"
    }
    garages {
        uuid id PK
        text name
    }
    roles {
        uuid id PK
        uuid garage_id FK
        text name "UNIQUE per garage"
        text_array scopes "e.g. {staff:manage} or {*}"
        bool is_system "Owner = true, immutable"
    }
    internal_user_profiles_garages {
        uuid internal_user_profile_id PK,FK
        uuid garage_id PK,FK
        uuid role_id FK "composite FK (role_id, garage_id)"
    }
    invitations {
        uuid id PK
        uuid garage_id FK
        text phone "UNIQUE per garage"
        uuid role_id FK "composite FK (role_id, garage_id)"
    }
```

Key point: a user can belong to **many garages** and hold a **different role in
each** (the role lives on the membership row, not the user). New members join via
a **phone-number invitation**: a staff manager creates a pending `invitations`
row; the invitee accepts it once signed in (their phone must match), which turns
it into a membership.

---

## 6. The escalation guard (why owners are special)

You can only grant scopes you already hold. Since the wildcard grants everything,
**only an owner can create another owner.**

```mermaid
flowchart TD
    A["Actor assigns Role X to a member<br/>(or defines Role X's scopes)"] --> B{"for each scope in Role X:<br/>does actor hold it? (wildcard counts)"}
    B -- "all held" --> OK["✅ allowed"]
    B -- "any missing" --> NO["403 - cannot grant a scope you don't hold"]
```

---

## Cheat sheet

| Concept | Rule |
| --- | --- |
| Permission check | Always `Scope.Grants(...)` — never a role name |
| Identity token | `InternalIdentityJwt`, ~60 min, no garage/scopes |
| Garage token | `GarageJwt`, ~5 min, carries `garage_id` + `scope` |
| Scope resolution | DB read **once** at session mint, baked into token |
| Gate an endpoint | `.RequireScope(Scope.X)` |
| Owner role | system, holds `*`, immutable |
| Grant limit | escalation guard — can't grant what you don't hold |
| Garage lockout | last-staff-manager guard blocks it |
| Invite | phone-number invitation; invitee accepts to join (matched on `users.phone`) |
| Errors | 404 not-found · 403 escalation · 409 conflict |
