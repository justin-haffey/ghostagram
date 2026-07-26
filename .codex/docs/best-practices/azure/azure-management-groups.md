# Azure Management Group Structure — Ghostworx.ai Inc

This document describes the observed **Azure Management Group hierarchy** for Ghostworx.ai Inc, based on the provided Azure Portal configuration.  
The purpose of this hierarchy is to apply consistent governance, policy inheritance, and access control across multiple subscriptions and environments.

---

## 1. Tenant Root Group

**Name:** Tenant Root Group  
**Type:** Management Group  
**ID:** 2cb58feb-6f3f-4d06-bdb9-c23ddf764bd9  

The Tenant Root Group represents the top-level node for the Azure Active Directory tenant. All other management groups are nested beneath it. Governance policies applied here cascade down to all child management groups unless explicitly overridden.

---

## 2. Ghostworx (Parent Management Group)

**Name:** Ghostworx  
**Type:** Management Group  
**ID:** GWX 

The **Ghostworx** group serves as the primary organizational container under the Tenant Root Group. It divides the environment into operational categories and lifecycle states.

### Child Groups

1. **Decommissioned** — For deprecated or retired environments.
2. **Landing Zones** — For structured deployment environments following Azure Landing Zone architecture.
3. **Platform** — For shared infrastructure and services that support all workloads.
4. **Sandbox** — For experimentation and testing.

---

## 3. Decommissioned

**Purpose:**  
Contains subscriptions and resources that have reached end-of-life. Used for controlled deactivation, cleanup, and auditing.

**ID:** Decommissioned  

**Governance Implications:**  
Policies may include restricted write access, cost controls, and tagging for archival.

---

## 4. Landing Zones

**Purpose:**  
Implements **Azure Landing Zone** principles for environment separation and baseline governance. Subdivided by function and business purpose.

**ID:** Landing-Zones  

### Child Groups

- **Client** — Likely used for customer-facing workloads or isolated client deployments.
- **Corp** — Used for internal enterprise workloads, applications, and productivity systems.

---

## 5. Platform

**Purpose:**  
Hosts shared services and foundational resources that multiple workloads depend on (networking, identity, monitoring, etc.).

**ID:** Platform

### Child Groups

- **Automation** — For infrastructure automation tools (e.g., Azure Automation, DevOps agents).
- **Connectivity** — For networking hubs, ExpressRoute, VPN gateways.
- **Intelligence** — Shared AI/ML platform services
- **Management** — Centralized operations such as logging, monitoring, and backup.

---

## 6. Sandbox

**Purpose:**  
Environment for development, testing, or proof-of-concept projects.  
Policies here are often relaxed for agility but still governed to control costs.

**ID:** Sandbox  

**Governance Notes:**  
Might include automatic cost limits, resource locks, or lifecycle policies.

---

## 7. Summary of Hierarchy

```structure
Tenant Root Group
└── Ghostworx
    ├── Decommissioned
    ├── Landing Zones
    │   ├── Public
    │   └── Private
    ├── Platform
    │   ├── Automation
    │   ├── Connectivity
    │   ├── Intelligence
    │   └── Management
    └── Sandbox
```

---