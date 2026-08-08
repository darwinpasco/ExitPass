import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { OperatorConsoleAuthenticationShell } from "./OperatorConsoleAuthenticationShell";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <OperatorConsoleAuthenticationShell />
  </StrictMode>
);
