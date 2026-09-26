import * as db from "./database";
import * as crypto from "crypto";
import * as fs from "fs";
import * as http from "http";
import * as path from "path";
import app from "./web";
import WebSocketManager from "./sockets";
const PORT = process.env.PORT || 51001;

(async() => {
    // Without a configured secret, create one and keep it next to the database. The secret
    // signs the tokens Conduit uses, so it has to stay the same for codes to stay valid.
    if (!process.env.RIFT_JWT_SECRET) {
        const secretPath = path.join(path.dirname(db.DATABASE_PATH), "jwt-secret");
        if (!fs.existsSync(secretPath)) {
            fs.writeFileSync(secretPath, crypto.randomBytes(32).toString("hex"), { mode: 0o600 });
            console.log("[+] Created a new JWT secret at " + secretPath);
        }

        process.env.RIFT_JWT_SECRET = fs.readFileSync(secretPath, "utf8").trim();
    }

    console.log("[+] Starting rift...");
    db.create();

    const server = http.createServer(app);

    const sockets = new WebSocketManager();
    server.on("upgrade", sockets.handleUpgradeRequest);

    console.log("[+] Listening on 0.0.0.0:" + PORT + "... ^C to exit.");
    server.listen(PORT);
})();