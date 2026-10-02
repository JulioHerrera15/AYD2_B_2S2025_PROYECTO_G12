"""Locust para el recorrido de compra completo de SGO-Logic (carga tipo Black Friday).

Ver documentacion/docs/PRUEBAS-UI.md (seccion "Bloque 2") para la guia
completa: plan de carga escalonado, calculo de tasa (N/T), que mirar en
Grafana/Locust, y la decision documentada sobre como repartir el inventario
entre SKUs para no agotarlo con esta carga.

Que simula, por usuario virtual:

  1. on_start (UNA sola vez por usuario virtual, no cuenta para la tasa de
     ordenes/segundo del bucle de compra):
     - POST /Auth/Registro con un correo unico.
     - Lee el codigo de verificacion de 6 digitos desde el mock de
       mensajeria (GET http://localhost:8089/api/mensajes), FUERA de las
       estadisticas de Locust: no es parte de "la web", es el estand-in de
       "revisar el correo".
     - POST /Auth/Verificar y POST /Auth/Login (guarda la cookie sgo_jwt en
       la sesion de este usuario virtual).

     Por que registrar un usuario nuevo en vez de reusar cliente1/2/3@sgo.local:
     el carrito vive en Redis bajo la clave cart:{sub del JWT}. Con cientos de
     usuarios virtuales concurrentes compartiendo solo 3 cuentas, sus carritos
     se pisarian entre si y la prueba mediria una condicion de carrera del
     propio test, no del sistema.

  2. @task comprar() (en bucle, esto SI es lo que determina la tasa de
     ordenes/segundo -- ver PRUEBAS-UI.md seccion 3.4):
     - GET /Catalogo (navegar).
     - GET /Catalogo/Stock?sku=... (consultar existencias de un SKU al azar).
     - POST /Carrito/Agregar (agregarlo al carrito).
     - GET /Checkout (abrir la pagina de pago) para leer el token antiforgery
       del formulario, igual que el navegador.
     - POST /Checkout/Tokenizar con la tarjeta y el header
       RequestVerificationToken: la Web la pasa a Pagos (POST
       /api/pagos/tokens), que la valida, la cifra en su boveda transitoria y
       devuelve un token opaco. Asi lo hace el JS del navegador real (ver
       Views/Checkout/Index.cshtml, URL_TOKENIZACION); el navegador ya no habla
       con PayKlik. OJO: si esto se omite, Checkout/Procesar devuelve el
       formulario con el error "Valida tu tarjeta antes de confirmar el
       pedido" y ninguna orden se crea.
     - POST /Checkout/Procesar con el token obtenido (crea + confirma la
       orden del lado del servidor; el endpoint real setup ya reintenta la
       confirmacion una vez si falla).

  Decision documentada sobre el inventario (PRUEBAS-UI.md 3.6): se compra al
  azar entre 6 de los 8 SKUs del catalogo, siempre con entrega a DOMICILIO
  (reserva contra los centros de distribucion, ~200 unidades/SKU, el mayor
  margen disponible). Se EXCLUYEN a proposito PROD-001 y PROD-005: estan
  sembrados con stock deliberadamente bajo (3 y 0 unidades) para la prueba de
  sobreventa manual de la guia; si esta carga los toca, arruina ese escenario.

  Este script mide la experiencia de navegacion+checkout bajo carga (ese es
  el foco del enunciado), no el desenlace final de la saga (si el cobro se
  aplico, si la factura se certifico). Eso ya lo verifica
  scripts/pruebas-carga/eac01-hu023-carga.js con k6. Por eso una llamada a
  /Checkout/Procesar se cuenta como exito de Locust en cuanto llega a la
  pantalla /Checkout/Exito, sin esperar el resultado asincrono del cobro.
"""

import random
import re
import time
import uuid
from datetime import datetime, timezone

import requests
from locust import HttpUser, between, task
from locust.exception import StopUser

# El mock de mensajeria no esta detras de la Web ni del Gateway en este
# recorrido: se consulta directo, como estand-in de "revisar el correo".
MENSAJERIA_URL = "http://localhost:8089/api/mensajes"

# El navegador real tokeniza la tarjeta contra la propia Web (que la pasa a
# Pagos), con el token antiforgery que trae el formulario del checkout. Ver
# Views/Checkout/Index.cshtml, constante URL_TOKENIZACION.
PATRON_ANTIFORGERY = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
TITULAR_PRUEBA = "Comprador Locust"

# Numero de tarjeta de prueba que pasa la validacion de Luhn de Pagos
# (igual al que ya usa scripts/pruebas-carga/eac01-hu023-carga.js).
NUMERO_TARJETA_PRUEBA = "4111111111111111"

# Subconjunto de PROD-002..PROD-008 (sku, nombre, precio) del catalogo de
# Inventario (inventario.producto). Se excluyen PROD-001 y PROD-005 a proposito
# (ver docstring del modulo). El precio solo se manda porque el formulario lo
# trae: Auth lo ignora y toma el vigente de Inventario.
CATALOGO_PARA_CARGA = [
    {"sku": "PROD-002", "nombre": 'Smart Ultra HD LED TV 65"', "precio": "5499.00"},
    {"sku": "PROD-003", "nombre": "Lavadora de Carga Frontal EcoBubble 22kg", "precio": "7899.00"},
    {"sku": "PROD-004", "nombre": "Barra de Sonido Dolby Atmos 5.1.2 550W", "precio": "3299.00"},
    {"sku": "PROD-006", "nombre": "Aire Acondicionado Inverter 18000 BTU", "precio": "4599.00"},
    {"sku": "PROD-007", "nombre": "Microondas Grill Smart Inverter 42L", "precio": "1899.00"},
    {"sku": "PROD-008", "nombre": "Aspiradora Robot Láser con Fregado", "precio": "3899.00"},
]

NOMBRES = ["Ana", "Carlos", "Maria", "Jose", "Lucia", "Diego", "Sofia", "Pedro", "Valeria", "Luis"]
APELLIDOS = ["Lopez", "Mendez", "Garcia", "Ramirez", "Castillo", "Morales", "Ortiz", "Reyes"]

PATRON_CODIGO = re.compile(r"\b\d{6}\b")


class CompradorMegaStore(HttpUser):
    """Un usuario virtual = un comprador que se registra una vez y despues
    hace compras en bucle. host apunta a la Web (localhost:8092), no al
    Gateway ni a las APIs -- el objetivo es medir el recorrido tal como lo
    vive un cliente real en el navegador."""

    host = "http://localhost:8092"

    # Piensa entre 3 y 8 segundos antes de arrancar la siguiente compra
    # completa. Sumado a las pausas intra-tarea de mas abajo, esto es lo que
    # define el T del calculo N/T de PRUEBAS-UI.md seccion 3.4 -- no lo
    # tomen como el T exacto, midan el real con el propio panel de Locust.
    wait_time = between(3, 8)

    def on_start(self):
        sufijo = uuid.uuid4().hex[:10]
        self.email = f"carga-{sufijo}@megastore-locust.test"
        self.password = "Locust1234!"
        nombre = random.choice(NOMBRES)
        apellido = random.choice(APELLIDOS)

        resp = self.client.post(
            "/Auth/Registro",
            data={
                "nombre": nombre,
                "apellido": apellido,
                "email": self.email,
                "contrasena": self.password,
                "confirmarContrasena": self.password,
            },
            name="/Auth/Registro",
        )
        if resp.status_code >= 400:
            resp.failure(f"registro fallo: {resp.status_code}")
            raise StopUser()

        codigo = self._leer_codigo_verificacion()
        if codigo is None:
            raise StopUser()

        resp = self.client.post(
            "/Auth/Verificar",
            data={"email": self.email, "codigo": codigo},
            name="/Auth/Verificar",
        )
        if resp.status_code >= 400:
            resp.failure(f"verificacion fallo: {resp.status_code}")
            raise StopUser()

        resp = self.client.post(
            "/Auth/Login",
            data={"email": self.email, "contrasena": self.password},
            name="/Auth/Login",
        )
        if resp.status_code >= 400:
            resp.failure(f"login fallo: {resp.status_code}")
            raise StopUser()

    def _leer_codigo_verificacion(self):
        """Poll fuera de las estadisticas de Locust (requests, no self.client):
        el mock de mensajeria no es parte de la Web bajo prueba, es el
        estand-in de "revisar el correo"."""
        for _ in range(10):
            try:
                resp = requests.get(MENSAJERIA_URL, timeout=5)
                resp.raise_for_status()
                mensajes = resp.json()
            except (requests.RequestException, ValueError):
                mensajes = []

            for mensaje in reversed(mensajes):
                destinatario = mensaje.get("destinatario") or mensaje.get("Destinatario") or ""
                if destinatario.lower() != self.email.lower():
                    continue
                cuerpo = mensaje.get("cuerpo") or mensaje.get("Cuerpo") or ""
                coincidencia = PATRON_CODIGO.search(cuerpo)
                if coincidencia:
                    return coincidencia.group(0)

            time.sleep(0.5)

        return None

    @task
    def comprar(self):
        self.client.get("/Catalogo", name="/Catalogo")
        time.sleep(random.uniform(1, 3))

        producto = random.choice(CATALOGO_PARA_CARGA)

        self.client.get(
            "/Catalogo/Stock",
            params={"sku": producto["sku"]},
            name="/Catalogo/Stock",
        )
        time.sleep(random.uniform(0.5, 1.5))

        resp_carrito = self.client.post(
            "/Carrito/Agregar",
            data={
                "sku": producto["sku"],
                "descripcion": producto["nombre"],
                "cantidad": 1,
                "precioUnitario": producto["precio"],
            },
            headers={"X-Requested-With": "XMLHttpRequest"},
            name="/Carrito/Agregar",
        )
        if resp_carrito.status_code >= 400:
            return
        time.sleep(random.uniform(1, 2))

        token = self._tokenizar_tarjeta()
        if token is None:
            return
        time.sleep(random.uniform(2, 4))

        self._confirmar_compra(token)

    def _tokenizar_tarjeta(self):
        # Abrir el checkout como el navegador: trae el token antiforgery que exige
        # /Checkout/Tokenizar (la cookie antiforgery queda en la sesion de Locust).
        with self.client.get("/Checkout", name="/Checkout", catch_response=True) as pagina:
            coincidencia = PATRON_ANTIFORGERY.search(pagina.text or "")
            if coincidencia is None:
                pagina.failure("el checkout no trajo el token antiforgery")
                return None
            pagina.success()

        anio_expiracion = datetime.now(timezone.utc).year + 3
        with self.client.post(
            "/Checkout/Tokenizar",
            data={
                "numero": NUMERO_TARJETA_PRUEBA,
                "mesExp": 12,
                "anioExp": anio_expiracion,
                "cvv": "123",
                "titular": TITULAR_PRUEBA,
                "guardar": "false",
            },
            headers={"RequestVerificationToken": coincidencia.group(1), "Accept": "application/json"},
            name="/Checkout/Tokenizar",
            catch_response=True,
        ) as resp:
            if resp.status_code == 200:
                resp.success()
                try:
                    return resp.json().get("token")
                except ValueError:
                    return None
            resp.failure(f"tokenizacion fallo: {resp.status_code}")
            return None

    def _confirmar_compra(self, token):
        with self.client.post(
            "/Checkout/Procesar",
            data={
                "metodoEntrega": "DOMICILIO",
                "departamento": "Guatemala",
                "municipio": "Guatemala",
                "direccion": "Ciudad de Guatemala",
                "medioPago": "TARJETA",
                "tokenMedioPago": token,
            },
            name="/Checkout/Procesar",
            catch_response=True,
        ) as resp:
            if "/Checkout/Exito" in resp.url:
                resp.success()
            else:
                resp.failure(f"no llego a /Checkout/Exito (termino en {resp.url})")