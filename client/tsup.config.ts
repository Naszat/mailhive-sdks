import { defineConfig } from "tsup";

export default defineConfig([
  {
    entry: { index: "src/index.ts", react: "src/react.ts" },
    format: ["esm", "cjs"],
    dts: true,
    sourcemap: true,
    clean: true,
    target: "es2020",
    external: ["react"],
  },
  {
    // The <script> tag build: one file, no imports, a MailhiveForms global.
    entry: { embed: "src/embed.ts" },
    format: ["iife"],
    globalName: "MailhiveForms",
    minify: true,
    target: "es2020",
    outExtension: () => ({ js: ".js" }),
  },
]);
