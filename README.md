# ExitPass

ExitPass is a cashierless parking payment and exit authorization platform built for multi-site parking operations.

It enables parkers to settle parking fees through Web Pay, AutoPay stations, partner payment channels, and future external integrations, while preserving the Vendor PMS as the sole authority for parking session state, amount due, and exit eligibility.

## Purpose

This repository contains the engineering assets for ExitPass, including:

- system services
- shared building blocks
- infrastructure and container assets
- database migration assets
- mock integrations and simulators
- automated tests
- architecture and engineering documentation

## Core Design Position

ExitPass is not a standalone parking management system.

The Vendor PMS remains the authoritative source for:

- session state
- tariff computation and amount due
- discount and override decisions handled within vendor rules
- exit eligibility
- operational audit records owned by the vendor domain

ExitPass acts as the orchestration and control layer around payment, exit authorization, integration, observability, and operational resilience.

## Repository Strategy

This repository is a private monorepo.

That is intentional.

ExitPass is a multi-service platform with tightly coupled domain rules, shared infrastructure, shared contracts, shared test fixtures, and strict system invariants. A monorepo keeps those assets aligned and reduces design drift during the engineering phase.

## Build Strategy

ExitPass is built in phases.

We are not building every service in full at once. We are building in authority-chain order so the early slices remain consistent with the BRD, system design, database design, and API contracts.

### Early implementation priority

1. Central PMS service foundation
2. PostgreSQL schema, migrations, and core database functions
3. Payment attempt orchestration slice
4. Outbox and event publication foundation
5. Mock Vendor PMS and mock payment provider
6. API gateway and external entry points
7. Additional services and operational tooling

## Top-Level Repository Structure

```text
ExitPass/
├── .github/               # CI/CD workflows, PR templates, issue templates
├── docs/                  # BRD, SDD, DB design, API contracts, runbooks, ADRs
├── infra/                 # Docker, nginx, certs, observability, scripts, DB assets
├── mocks/                 # Mock Vendor PMS, mock payment provider, simulators, payloads
├── src/                   # Production source code
├── tests/                 # Unit, integration, E2E, security, performance tests
├── .dockerignore
├── .editorconfig
├── .env.example
├── .gitattributes
├── .gitignore
├── Directory.Build.props
├── Directory.Packages.props
├── ExitPass.sln
├── global.json
└── README.md
