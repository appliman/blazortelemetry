---
title: "Correct the README dashboard embedding example"
kind: problem
status: pending
created: 2026-11-19
summary: "The embedding example references BlazorTelemetry.TelemetryDashboard, but the component is now in the standalone host under BlazorTelemetry.Host.Components.Dashboard. Verify whether embedding is supported and update the guide accordingly."
---

## Why
`README.md:213` references `BlazorTelemetry.TelemetryDashboard`. The Razor component is under the host, and its `_Imports.razor:8` declares `BlazorTelemetry.Host.Components.Dashboard`, so the type reference shown in the example cannot resolve against the current solution.

## What to do
Verify whether embedding is still supported. If it is, document the required project reference, namespace, and services and validate the example; if not, replace the obsolete instructions with the supported deployment approach.

## Where
`README.md` (Embed the dashboard in a Blazor application) and `src/BlazorTelemetry.Host/Components/Dashboard/`.
