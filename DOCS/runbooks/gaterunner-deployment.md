# GateRunner Deployment Runbook

## Purpose

Use this runbook to build and publish GateRunner release artifacts.

## Publish Command

```powershell
powershell -ExecutionPolicy Bypass -File D:\GateRunner\Scripts\Publish-GateRunner.ps1 -Version 1.0.0
```

## Output

The script writes:

```text
D:\GateRunner\artifacts\GateRunner\<version>\wpf
D:\GateRunner\artifacts\GateRunner\<version>\cli
```

The WPF artifact is framework-dependent `win-x86`. The target machine must have the .NET 8 desktop runtime installed.
The `artifacts` directory is local/CI-generated output and is not checked into source control.

## Deployment Strategy

Current strategy: ship GateRunner independently from the SI360 installer/updater flow. This keeps release gate tooling decoupled from application deployment and avoids creating a circular dependency where the app installer is needed before the release gate can run.

Future installer integration can copy the published WPF artifact into the SI360 tools bundle after GateRunner has a stable release cadence.

## Versioning

The publish script passes `Version`, `AssemblyVersion`, and `FileVersion` MSBuild properties. The version appears in report environment metadata through assembly version discovery.
