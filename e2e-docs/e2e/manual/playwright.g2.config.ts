// Configuração do grupo de fotos g2: escolhe o spec do grupo e reaproveita a configuração instalada pelo plugin.
import { criarConfigDeFotos } from "../documentacao/playwright.fotos.config.ts";

export default criarConfigDeFotos({ testMatch: "g2-*.fotos.spec.ts", outputDir: "test-results/g2" });
