"""
Mock de API REST para el ApiExtractor del Worker Service.
Uso:
    python mock_api.py
Luego, en otra terminal, antes de "dotnet run":
    $env:ETL_COMMENTS_API_URL = "http://localhost:5000/"
    $env:ETL_COMMENTS_API_PATH = "/comments"
    $env:ETL_COMMENTS_API_KEY = "local-dev-key"
"""

import csv
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

API_KEY = "local-dev-key"
PORT = 5000
CSV_PATH = Path(__file__).parent / "Data" / "social_comments.csv"


def load_comments() -> list[dict]:
    """Lee social_comments.csv y lo transforma al contrato que espera ApiCommentItem en C#:
    Id, ClientId (nullable), ProductId, CreatedAt, Comment, Score.
    Los comentarios sociales no traen puntaje, asi que Score siempre es null."""
    if not CSV_PATH.exists():
        print(f"[mock_api] ADVERTENCIA: no se encontro {CSV_PATH}", file=sys.stderr)
        return []

    items = []
    with CSV_PATH.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            id_cliente = (row.get("IdCliente") or "").strip()
            items.append({
                "Id": row["IdComment"].strip(),
                "ClientId": id_cliente if id_cliente else None,
                "ProductId": row["IdProducto"].strip(),
                "CreatedAt": f"{row['Fecha'].strip()}T00:00:00Z",
                "Comment": row["Comentario"],
                "Score": None,
            })
    return items


COMMENTS = load_comments()


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = urlparse(self.path).path

        if path != "/comments":
            self._send_json(404, {"error": f"Ruta no encontrada: {path}"})
            return

        if self.headers.get("X-Api-Key") != API_KEY:
            self._send_json(401, {"error": "X-Api-Key invalida o ausente"})
            return

        self._send_json(200, COMMENTS)

    def _send_json(self, status: int, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        print(f"[mock_api] {self.address_string()} - {format % args}")


if __name__ == "__main__":
    print(f"[mock_api] {len(COMMENTS)} comentarios cargados desde {CSV_PATH}")
    print(f"[mock_api] Escuchando en http://localhost:{PORT}/comments  (X-Api-Key: {API_KEY})")
    server = ThreadingHTTPServer(("localhost", PORT), Handler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[mock_api] Detenido.")