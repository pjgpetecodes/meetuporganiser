---
name: bicep-infra-manager
description: Skill for configuring, validating, and updating Azure Bicep infrastructure modules.
---

# Bicep Infrastructure Manager Skill

## When to Use
Use this skill when provisioning, editing, or validating Azure cloud resources for Meetup Organiser.

## Best Practices
1. **Serverless SQL**: Always set `autoPauseDelay: 60` and `minCapacity: json('0.5')` to guarantee minimal operational cost during inactivity.
2. **App Service**:
   - Enable system-assigned managed identity.
   - Use App Insights connection string injection via `APPLICATIONINSIGHTS_CONNECTION_STRING`.
   - Disable remote FTP/FTPS if not required.
3. **Storage Security**:
   - Container `templates` is private (`None`).
   - Containers `speaker-photos` and `generated-assets` permit public blob read (`Blob`).
4. **Key Vault RBAC**:
   - Enable `enableRbacAuthorization: true`.
   - Grant the Web App managed identity the `Key Vault Secrets User` role definition (`4633458b-17de-408a-b874-0445c86b69e6`).
