import type { PortalConfig, PortalKind } from "./types";

const apiBase = {
  catalogUrl: "http://localhost:8081",
  inventoryUrl: "http://localhost:8082",
  ordersUrl: "http://localhost:8083",
  paymentsUrl: "http://localhost:8084"
};

const keycloakBaseUrl = "http://localhost:8090";
const realm = "ecommerce-platform";

const portalConfigs: Record<PortalKind, PortalConfig> = {
  customer: {
    kind: "customer",
    title: "Customer Commerce",
    subtitle: "Catalog browsing and order placement for ecommerce validation flows.",
    themeName: "Saffron Flow",
    accent: "#d06f1b",
    auth: {
      url: keycloakBaseUrl,
      realm,
      clientId: "customer-web",
      allowedRoles: ["customer", "platform-admin"]
    },
    api: apiBase
  },
  operations: {
    kind: "operations",
    title: "Operations Console",
    subtitle: "Catalog, inventory and order operations in one streamlined console.",
    themeName: "Harbor Grid",
    accent: "#0f7a7a",
    auth: {
      url: keycloakBaseUrl,
      realm,
      clientId: "operations-web",
      allowedRoles: ["catalog-manager", "inventory-manager", "order-manager", "platform-admin"]
    },
    api: apiBase
  },
  admin: {
    kind: "admin",
    title: "Admin & Finance Hub",
    subtitle: "Financial traceability, payment diagnostics and platform visibility.",
    themeName: "Ledger Brass",
    accent: "#9b3d22",
    auth: {
      url: keycloakBaseUrl,
      realm,
      clientId: "admin-web",
      allowedRoles: ["finance-analyst", "platform-admin"]
    },
    api: apiBase
  }
};

export function buildPortalConfig(): PortalConfig {
  const rawPortalKind = import.meta.env.VITE_PORTAL_KIND;
  const portalKind: PortalKind =
    rawPortalKind === "customer" || rawPortalKind === "operations" || rawPortalKind === "admin"
      ? rawPortalKind
      : "customer";

  return portalConfigs[portalKind];
}
