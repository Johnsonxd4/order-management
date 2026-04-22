import Keycloak from "keycloak-js";

export async function apiGet<T>(url: string, keycloak: Keycloak): Promise<T> {
  await keycloak.updateToken(30);

  const response = await fetch(url, {
    headers: {
      Authorization: `Bearer ${keycloak.token ?? ""}`
    }
  });

  if (!response.ok) {
    throw new Error(`GET ${url} failed with status ${response.status}`);
  }

  return (await response.json()) as T;
}

export async function apiPost<TResponse, TRequest>(
  url: string,
  payload: TRequest,
  keycloak: Keycloak,
  method = "POST"
): Promise<TResponse> {
  await keycloak.updateToken(30);

  const response = await fetch(url, {
    method,
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${keycloak.token ?? ""}`
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(`${method} ${url} failed with status ${response.status}: ${message}`);
  }

  return (await response.json()) as TResponse;
}
