import "@testing-library/jest-dom/vitest";
import { vi } from "vitest";

vi.stubEnv("VITE_WEBPAY_PUBLIC_BASE_URL", "https://webpay.example.test");
