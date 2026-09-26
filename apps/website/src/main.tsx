import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import Landing from "./Landing";
import "./design.css";

createRoot(document.getElementById("root")!).render(<StrictMode><Landing /></StrictMode>);
