// Configuração do vídeo do manual de exportações: specs narráveis em e2e/manual/video.
import { criarConfigDeVideo } from "../documentacao/playwright.video.config.ts";

export default criarConfigDeVideo({ testMatch: "**/*.video.spec.ts", outputDir: "test-results/video" });
