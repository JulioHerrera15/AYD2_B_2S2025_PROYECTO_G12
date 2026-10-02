using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Productos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "producto",
                schema: "inventario",
                columns: table => new
                {
                    sku = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    precio = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    icono = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    etiqueta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_producto", x => x.sku);
                });

            migrationBuilder.InsertData(
                schema: "inventario",
                table: "producto",
                columns: new[] { "sku", "activo", "categoria", "descripcion", "etiqueta", "icono", "nombre", "precio" },
                values: new object[,]
                {
                    { "PROD-001", true, "Línea Blanca", "Refrigeradora inteligente de 28 pies cúbicos, compuerta French Door, panel táctil Wi-Fi y enfriamiento Twin Cooling Plus.", "Más Vendido", "kitchen", "Lumina Smart Refrigerator Pro", 14999.00m },
                    { "PROD-002", true, "Televisores", "Pantalla 4K UHD con HDR10+, tasa de refresco de 120Hz nativos, Dolby Atmos y sistema operativo Google TV integrado.", "Oferta", "tv", "Smart Ultra HD LED TV 65\"", 5499.00m },
                    { "PROD-003", true, "Línea Blanca", "Motor Digital Inverter con 10 años de garantía, lavado con vapor higiénico y dispensador automático de detergente.", "Nuevo", "local_laundry_service", "Lavadora de Carga Frontal EcoBubble 22kg", 7899.00m },
                    { "PROD-004", true, "Audio", "Subwoofer inalámbrico potente, altavoces traseros satelitales, HDMI eARC y compatibilidad con AirPlay 2 y Spotify Connect.", "Popular", "speaker", "Barra de Sonido Dolby Atmos 5.1.2 550W", 3299.00m },
                    { "PROD-005", true, "Informática", "Procesador AMD Ryzen 9 7945HX, GPU NVIDIA RTX 4070 8GB, 32GB RAM DDR5, 1TB NVMe Gen4 y pantalla 240Hz QHD.", "Gamer", "laptop_chromebook", "Laptop Gamer Ultra 16\" Ryzen 9", 16499.00m },
                    { "PROD-006", true, "Climatización", "Tecnología WindFree silenciosa, filtro antibacteriano de alta densidad, modo frío/calor y control remoto por aplicación móvil.", "Ahorro Energético", "ac_unit", "Aire Acondicionado Inverter 18000 BTU", 4599.00m },
                    { "PROD-007", true, "Línea Blanca", "Capacidad de 1.5 pies cúbicos, recubrimiento EasyClean antibacteriano, función de dorador al carbón y descongelado uniforme.", "Básico", "microwave", "Microondas Grill Smart Inverter 42L", 1899.00m },
                    { "PROD-008", true, "Hogar Inteligente", "Navegación LiDAR de alta precisión, succión de 5000Pa, mapeo multinivel en 3D y estación de autovaciado de polvo.", "Smart", "cleaning_services", "Aspiradora Robot Láser con Fregado", 3899.00m },
                    { "PROD-101", true, "Audio", "Cancelación activa de ruido híbrida, 40 horas de batería, audio espacial y carga rápida USB-C de 10 minutos para 5 horas.", "Últimas Unidades", "headphones", "Audífonos Inalámbricos ANC Pro", 1299.00m },
                    { "PROD-102", true, "Informática", "Pantalla OLED de 120Hz, procesador octa-core, 8GB RAM, lápiz óptico incluido y conectividad Wi-Fi 6E.", "Stock Limitado", "tablet_mac", "Tablet 11\" OLED 256GB", 4299.00m },
                    { "PROD-103", true, "Informática", "Panel VA curvo 1500R QHD, 165Hz con 1ms MPRT, FreeSync Premium y HDR400 para juegos competitivos.", "Exclusivo", "monitor", "Monitor Gamer Curvo 32\" 165Hz", 2899.00m },
                    { "PROD-104", true, "Climatización", "Extrae hasta 30 litros diarios, control por aplicación, sensor de humedad automático y modo secado de ropa.", "Temporada", "humidity_percentage", "Deshumidificador Inteligente 30L", 2199.00m },
                    { "PROD-105", true, "Hogar Inteligente", "Resolución 2K con visión nocturna a color, detección de personas, audio bidireccional y grabación en la nube.", "Seguridad", "videocam", "Cámara de Seguridad Wi-Fi 2K", 699.00m },
                    { "SKU-DEMO-001", true, "Demo", "Artículo de demostración con existencias en CD-CENTRAL y TIENDA-01-CENTRAL.", "Demo", "inventory_2", "Producto de demostración 1", 100.00m },
                    { "SKU-DEMO-002", true, "Demo", "Artículo de demostración con existencias en CD-CENTRAL.", "Demo", "inventory_2", "Producto de demostración 2", 250.00m },
                    { "SKU-DEMO-003", true, "Demo", "Artículo de demostración con existencias en TIENDA-01-CENTRAL.", "Demo", "inventory_2", "Producto de demostración 3", 75.00m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "producto",
                schema: "inventario");
        }
    }
}
