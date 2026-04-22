import { useEffect, useMemo, useState } from "react";
import Keycloak from "keycloak-js";
import { apiGet, apiPost } from "./api";
import type { InventoryItem, Order, Payment, PortalConfig, PortalRole, Product } from "./types";

interface AppProps {
  keycloak: Keycloak;
  portal: PortalConfig;
}

export function App({ keycloak, portal }: AppProps) {
  const [products, setProducts] = useState<Product[]>([]);
  const [stocks, setStocks] = useState<InventoryItem[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [payments, setPayments] = useState<Payment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [orderResult, setOrderResult] = useState<Order | null>(null);

  const roles = useMemo(() => {
    const realmRoles = (keycloak.tokenParsed?.realm_access?.roles ?? []) as PortalRole[];
    return realmRoles;
  }, [keycloak.tokenParsed]);

  const allowed = roles.some((role) => portal.auth.allowedRoles.includes(role));

  useEffect(() => {
    if (!allowed) {
      setLoading(false);
      return;
    }

    async function loadData() {
      try
      {
        setLoading(true);

        const requests: Promise<unknown>[] = [
          apiGet<Product[]>(`${portal.api.catalogUrl}/api/products`, keycloak).then(setProducts)
        ];

        if (portal.kind !== "customer") {
          requests.push(apiGet<InventoryItem[]>(`${portal.api.inventoryUrl}/api/stocks`, keycloak).then(setStocks));
          requests.push(apiGet<Order[]>(`${portal.api.ordersUrl}/api/orders`, keycloak).then(setOrders));
        }

        if (portal.kind === "admin") {
          requests.push(apiGet<Payment[]>(`${portal.api.paymentsUrl}/api/payments`, keycloak).then(setPayments));
        }

        await Promise.all(requests);
        setError(null);
      }
      catch (loadError)
      {
        setError(loadError instanceof Error ? loadError.message : "Failed to load portal data.");
      }
      finally
      {
        setLoading(false);
      }
    }

    void loadData();
  }, [allowed, keycloak, portal]);

  if (!allowed) {
    return (
      <div className="portal-shell portal-shell--forbidden" style={{ ["--portal-accent" as string]: portal.accent }}>
        <section className="hero-card">
          <span className="eyebrow">{portal.themeName}</span>
          <h1>{portal.title}</h1>
          <p>This frontend is restricted to roles: {portal.auth.allowedRoles.join(", ")}.</p>
          <p>Your current roles: {roles.length > 0 ? roles.join(", ") : "none"}.</p>
          <button className="ghost-button" onClick={() => keycloak.logout()}>
            Sign out
          </button>
        </section>
      </div>
    );
  }

  return (
    <div className={`portal-shell portal-shell--${portal.kind}`} style={{ ["--portal-accent" as string]: portal.accent }}>
      <header className="hero-card">
        <div>
          <span className="eyebrow">{portal.themeName}</span>
          <h1>{portal.title}</h1>
          <p>{portal.subtitle}</p>
        </div>
        <div className="identity-card">
          <div className="identity-name">{keycloak.tokenParsed?.preferred_username ?? "unknown-user"}</div>
          <div className="identity-roles">{roles.join(" · ")}</div>
          <button className="ghost-button" onClick={() => keycloak.logout()}>
            Logout
          </button>
        </div>
      </header>

      {loading ? <section className="panel">Loading portal data...</section> : null}
      {error ? <section className="panel panel--danger">{error}</section> : null}

      {portal.kind === "customer" ? (
        <CustomerPortal
          products={products}
          keycloak={keycloak}
          portal={portal}
          onOrderCreated={setOrderResult}
          orderResult={orderResult}
        />
      ) : null}

      {portal.kind === "operations" ? (
        <OperationsPortal
          keycloak={keycloak}
          portal={portal}
          products={products}
          stocks={stocks}
          orders={orders}
          onRefresh={async () => {
            setProducts(await apiGet<Product[]>(`${portal.api.catalogUrl}/api/products`, keycloak));
            setStocks(await apiGet<InventoryItem[]>(`${portal.api.inventoryUrl}/api/stocks`, keycloak));
            setOrders(await apiGet<Order[]>(`${portal.api.ordersUrl}/api/orders`, keycloak));
          }}
        />
      ) : null}

      {portal.kind === "admin" ? <AdminPortal orders={orders} payments={payments} /> : null}
    </div>
  );
}

function CustomerPortal({
  products,
  keycloak,
  portal,
  onOrderCreated,
  orderResult
}: {
  products: Product[];
  keycloak: Keycloak;
  portal: PortalConfig;
  onOrderCreated: (order: Order) => void;
  orderResult: Order | null;
}) {
  const [sku, setSku] = useState("SKU-CHAIR-001");
  const [quantity, setQuantity] = useState(1);
  const [paymentMethodToken, setPaymentMethodToken] = useState("tok_approved_1234");
  const [submitting, setSubmitting] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);

  async function submitOrder() {
    try {
      setSubmitting(true);
      const order = await apiPost<Order, object>(`${portal.api.ordersUrl}/api/orders`, {
        CustomerId: keycloak.tokenParsed?.preferred_username ?? "customer.demo",
        Currency: "BRL",
        PaymentMethodToken: paymentMethodToken,
        Lines: [
          {
            Sku: sku,
            Quantity: quantity
          }
        ]
      }, keycloak);

      onOrderCreated(order);
      setFeedback(`Order ${order.Id} created with status ${order.Status}.`);
    } catch (submitError) {
      setFeedback(submitError instanceof Error ? submitError.message : "Failed to create order.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      <section className="grid">
        <div className="panel panel--hero">
          <h2>Buy-side validation</h2>
          <p>Simulate a customer session and create an order directly against the ecommerce backend.</p>
          <div className="form-grid">
            <label>
              SKU
              <select value={sku} onChange={(event) => setSku(event.target.value)}>
                {products.map((product) => (
                  <option key={product.Id} value={product.Sku}>
                    {product.Sku} · {product.Name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Quantity
              <input type="number" min="1" value={quantity} onChange={(event) => setQuantity(Number(event.target.value))} />
            </label>
            <label className="form-grid__full">
              Payment token
              <input value={paymentMethodToken} onChange={(event) => setPaymentMethodToken(event.target.value)} />
            </label>
          </div>
          <div className="button-row">
            <button className="primary-button" disabled={submitting} onClick={() => void submitOrder()}>
              {submitting ? "Submitting..." : "Create validation order"}
            </button>
            <button className="ghost-button" onClick={() => setPaymentMethodToken("tok_declined_0000")}>
              Force payment decline
            </button>
          </div>
          {feedback ? <p className="feedback">{feedback}</p> : null}
        </div>

        <div className="panel">
          <h2>Catalog snapshot</h2>
          <div className="cards">
            {products.map((product) => (
              <article key={product.Id} className="mini-card">
                <div className="mini-card__title">{product.Name}</div>
                <div className="mini-card__meta">{product.Sku}</div>
                <div className="mini-card__price">
                  {product.Currency} {product.Price.toFixed(2)}
                </div>
              </article>
            ))}
          </div>
        </div>
      </section>

      {orderResult ? (
        <section className="panel">
          <h2>Last submitted order</h2>
          <pre>{JSON.stringify(orderResult, null, 2)}</pre>
        </section>
      ) : null}
    </>
  );
}

function OperationsPortal({
  keycloak,
  portal,
  products,
  stocks,
  orders,
  onRefresh
}: {
  keycloak: Keycloak;
  portal: PortalConfig;
  products: Product[];
  stocks: InventoryItem[];
  orders: Order[];
  onRefresh: () => Promise<void>;
}) {
  const [newSku, setNewSku] = useState("SKU-LAMP-001");
  const [newProductName, setNewProductName] = useState("Arc Lamp");
  const [newPrice, setNewPrice] = useState(599.9);
  const [newStock, setNewStock] = useState(15);
  const [message, setMessage] = useState<string | null>(null);
  const roles = (keycloak.tokenParsed?.realm_access?.roles ?? []) as string[];

  async function createProductAndSeedStock() {
    try {
      await apiPost<Product, object>(`${portal.api.catalogUrl}/api/products`, {
        Sku: newSku,
        Name: newProductName,
        Description: "Created from the operations portal",
        Price: newPrice,
        Currency: "BRL"
      }, keycloak);

      await apiPost<InventoryItem, object>(`${portal.api.inventoryUrl}/api/stocks/seed`, {
        Sku: newSku,
        AvailableQuantity: newStock
      }, keycloak);

      await onRefresh();
      setMessage(`Product ${newSku} created and stock seeded.`);
    } catch (createError) {
      setMessage(createError instanceof Error ? createError.message : "Operation failed.");
    }
  }

  return (
    <>
      <section className="grid grid--operations">
        <div className="panel">
          <h2>Operations permissions</h2>
          <p>Roles active in this session: {roles.join(", ")}</p>
          <p>The console surfaces catalog, stock and order monitoring in one operational workspace.</p>
        </div>

        <div className="panel">
          <h2>Create product + stock</h2>
          <div className="form-grid">
            <label>
              SKU
              <input value={newSku} onChange={(event) => setNewSku(event.target.value.toUpperCase())} />
            </label>
            <label>
              Product name
              <input value={newProductName} onChange={(event) => setNewProductName(event.target.value)} />
            </label>
            <label>
              Price
              <input type="number" min="1" step="0.01" value={newPrice} onChange={(event) => setNewPrice(Number(event.target.value))} />
            </label>
            <label>
              Available stock
              <input type="number" min="0" value={newStock} onChange={(event) => setNewStock(Number(event.target.value))} />
            </label>
          </div>
          <div className="button-row">
            <button className="primary-button" onClick={() => void createProductAndSeedStock()}>
              Push to platform
            </button>
            <button className="ghost-button" onClick={() => void onRefresh()}>
              Refresh views
            </button>
          </div>
          {message ? <p className="feedback">{message}</p> : null}
        </div>
      </section>

      <section className="grid grid--operations">
        <div className="panel">
          <h2>Catalog</h2>
          <table>
            <thead>
              <tr>
                <th>SKU</th>
                <th>Name</th>
                <th>Price</th>
              </tr>
            </thead>
            <tbody>
              {products.map((product) => (
                <tr key={product.Id}>
                  <td>{product.Sku}</td>
                  <td>{product.Name}</td>
                  <td>{product.Currency} {product.Price.toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="panel">
          <h2>Inventory</h2>
          <table>
            <thead>
              <tr>
                <th>SKU</th>
                <th>Available</th>
                <th>Reserved</th>
              </tr>
            </thead>
            <tbody>
              {stocks.map((item) => (
                <tr key={item.Sku}>
                  <td>{item.Sku}</td>
                  <td>{item.AvailableQuantity}</td>
                  <td>{item.ReservedQuantity}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="panel">
        <h2>Recent orders</h2>
        <table>
          <thead>
            <tr>
              <th>Order</th>
              <th>Customer</th>
              <th>Status</th>
              <th>Total</th>
            </tr>
          </thead>
          <tbody>
            {orders.map((order) => (
              <tr key={order.Id}>
                <td>{order.Id.slice(0, 8)}</td>
                <td>{order.CustomerId}</td>
                <td>{order.Status}</td>
                <td>{order.Currency} {order.TotalAmount.toFixed(2)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </>
  );
}

function AdminPortal({ orders, payments }: { orders: Order[]; payments: Payment[] }) {
  return (
    <>
      <section className="grid">
        <div className="panel panel--hero">
          <h2>Settlement visibility</h2>
          <p>Use this portal to correlate authorized orders with payment decisions and spot rejected flows.</p>
        </div>
        <div className="panel">
          <h2>Risk markers</h2>
          <div className="cards">
            <article className="mini-card">
              <div className="mini-card__title">Authorized orders</div>
              <div className="mini-card__price">{orders.filter((order) => order.Status === "authorized").length}</div>
            </article>
            <article className="mini-card">
              <div className="mini-card__title">Payment failures</div>
              <div className="mini-card__price">{orders.filter((order) => order.Status === "payment-failed").length}</div>
            </article>
            <article className="mini-card">
              <div className="mini-card__title">Declined payments</div>
              <div className="mini-card__price">{payments.filter((payment) => !payment.Approved).length}</div>
            </article>
          </div>
        </div>
      </section>

      <section className="grid">
        <div className="panel">
          <h2>Orders</h2>
          <table>
            <thead>
              <tr>
                <th>Order</th>
                <th>Status</th>
                <th>Total</th>
                <th>Failure</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.Id}>
                  <td>{order.Id.slice(0, 8)}</td>
                  <td>{order.Status}</td>
                  <td>{order.Currency} {order.TotalAmount.toFixed(2)}</td>
                  <td>{order.FailureReason ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="panel">
          <h2>Payments</h2>
          <table>
            <thead>
              <tr>
                <th>Payment</th>
                <th>Order</th>
                <th>Approved</th>
                <th>Amount</th>
              </tr>
            </thead>
            <tbody>
              {payments.map((payment) => (
                <tr key={payment.PaymentId}>
                  <td>{payment.TransactionId.slice(0, 14)}</td>
                  <td>{payment.OrderId.slice(0, 8)}</td>
                  <td>{payment.Approved ? "yes" : "no"}</td>
                  <td>{payment.Currency} {payment.Amount.toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}
