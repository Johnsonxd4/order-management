export type PortalKind = "customer" | "operations" | "admin";

export type PortalRole =
  | "customer"
  | "catalog-manager"
  | "inventory-manager"
  | "order-manager"
  | "finance-analyst"
  | "platform-admin";

export interface PortalConfig {
  kind: PortalKind;
  title: string;
  subtitle: string;
  themeName: string;
  accent: string;
  auth: {
    url: string;
    realm: string;
    clientId: string;
    allowedRoles: PortalRole[];
  };
  api: {
    catalogUrl: string;
    inventoryUrl: string;
    ordersUrl: string;
    paymentsUrl: string;
  };
}

export interface Product {
  Id: string;
  Sku: string;
  Name: string;
  Description?: string;
  Price: number;
  Currency: string;
  IsActive: boolean;
  UpdatedAtUtc: string;
}

export interface InventoryItem {
  Sku: string;
  AvailableQuantity: number;
  ReservedQuantity: number;
  UpdatedAtUtc: string;
}

export interface OrderLine {
  Sku: string;
  ProductName: string;
  Quantity: number;
  UnitPrice: number;
  LineTotal: number;
  Currency: string;
}

export interface Order {
  Id: string;
  CustomerId: string;
  Currency: string;
  Status: string;
  TotalAmount: number;
  CreatedAtUtc: string;
  PaymentTransactionId?: string;
  FailureReason?: string;
  Lines: OrderLine[];
}

export interface Payment {
  PaymentId: string;
  OrderId: string;
  TransactionId: string;
  Approved: boolean;
  Reason?: string;
  Amount: number;
  Currency: string;
}
