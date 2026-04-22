import React from "react";
import ReactDOM from "react-dom/client";
import Keycloak from "keycloak-js";
import { App } from "./portal/App";
import { buildPortalConfig } from "./portal/config";
import "./styles.css";

const portal = buildPortalConfig();

const keycloak = new Keycloak({
  url: portal.auth.url,
  realm: portal.auth.realm,
  clientId: portal.auth.clientId
});

async function bootstrap() {
  const authenticated = await keycloak.init({
    onLoad: "login-required",
    pkceMethod: "S256",
    checkLoginIframe: false
  });

  if (!authenticated) {
    return;
  }

  ReactDOM.createRoot(document.getElementById("root")!).render(
    <React.StrictMode>
      <App keycloak={keycloak} portal={portal} />
    </React.StrictMode>
  );
}

bootstrap().catch((error) => {
  console.error("Failed to bootstrap portal", error);
});
