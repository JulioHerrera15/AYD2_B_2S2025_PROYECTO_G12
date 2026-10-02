using SgoLogic.Inventario.Api.Domain;

namespace SgoLogic.Inventario.Api.Infrastructure.Persistencia;

/// <summary>
/// Catalogo inicial que carga la migracion Productos via HasData: exactamente los
/// productos que antes estaban fijos en SgoLogic.Web/Controllers/CatalogoController.cs
/// (PROD-001..008 y PROD-101..105, mismos nombres, categorias, precios, iconos,
/// descripciones y etiquetas), mas SKU-DEMO-001..003, que solo existian como
/// existencias en scripts/demo/sembrar-demo.sh y no tenian precio en ningun lado.
/// </summary>
internal static class ProductosSemilla
{
    public static IReadOnlyList<Producto> Todos { get; } = new[]
    {
        new Producto("PROD-001", "Lumina Smart Refrigerator Pro", "Línea Blanca", 14999.00m, "kitchen", true,
            "Refrigeradora inteligente de 28 pies cúbicos, compuerta French Door, panel táctil Wi-Fi y enfriamiento Twin Cooling Plus.",
            "Más Vendido"),

        new Producto("PROD-002", "Smart Ultra HD LED TV 65\"", "Televisores", 5499.00m, "tv", true,
            "Pantalla 4K UHD con HDR10+, tasa de refresco de 120Hz nativos, Dolby Atmos y sistema operativo Google TV integrado.",
            "Oferta"),

        new Producto("PROD-003", "Lavadora de Carga Frontal EcoBubble 22kg", "Línea Blanca", 7899.00m, "local_laundry_service", true,
            "Motor Digital Inverter con 10 años de garantía, lavado con vapor higiénico y dispensador automático de detergente.",
            "Nuevo"),

        new Producto("PROD-004", "Barra de Sonido Dolby Atmos 5.1.2 550W", "Audio", 3299.00m, "speaker", true,
            "Subwoofer inalámbrico potente, altavoces traseros satelitales, HDMI eARC y compatibilidad con AirPlay 2 y Spotify Connect.",
            "Popular"),

        new Producto("PROD-005", "Laptop Gamer Ultra 16\" Ryzen 9", "Informática", 16499.00m, "laptop_chromebook", true,
            "Procesador AMD Ryzen 9 7945HX, GPU NVIDIA RTX 4070 8GB, 32GB RAM DDR5, 1TB NVMe Gen4 y pantalla 240Hz QHD.",
            "Gamer"),

        new Producto("PROD-006", "Aire Acondicionado Inverter 18000 BTU", "Climatización", 4599.00m, "ac_unit", true,
            "Tecnología WindFree silenciosa, filtro antibacteriano de alta densidad, modo frío/calor y control remoto por aplicación móvil.",
            "Ahorro Energético"),

        new Producto("PROD-007", "Microondas Grill Smart Inverter 42L", "Línea Blanca", 1899.00m, "microwave", true,
            "Capacidad de 1.5 pies cúbicos, recubrimiento EasyClean antibacteriano, función de dorador al carbón y descongelado uniforme.",
            "Básico"),

        new Producto("PROD-008", "Aspiradora Robot Láser con Fregado", "Hogar Inteligente", 3899.00m, "cleaning_services", true,
            "Navegación LiDAR de alta precisión, succión de 5000Pa, mapeo multinivel en 3D y estación de autovaciado de polvo.",
            "Smart"),

        new Producto("PROD-101", "Audífonos Inalámbricos ANC Pro", "Audio", 1299.00m, "headphones", true,
            "Cancelación activa de ruido híbrida, 40 horas de batería, audio espacial y carga rápida USB-C de 10 minutos para 5 horas.",
            "Últimas Unidades"),

        new Producto("PROD-102", "Tablet 11\" OLED 256GB", "Informática", 4299.00m, "tablet_mac", true,
            "Pantalla OLED de 120Hz, procesador octa-core, 8GB RAM, lápiz óptico incluido y conectividad Wi-Fi 6E.",
            "Stock Limitado"),

        new Producto("PROD-103", "Monitor Gamer Curvo 32\" 165Hz", "Informática", 2899.00m, "monitor", true,
            "Panel VA curvo 1500R QHD, 165Hz con 1ms MPRT, FreeSync Premium y HDR400 para juegos competitivos.",
            "Exclusivo"),

        new Producto("PROD-104", "Deshumidificador Inteligente 30L", "Climatización", 2199.00m, "humidity_percentage", true,
            "Extrae hasta 30 litros diarios, control por aplicación, sensor de humedad automático y modo secado de ropa.",
            "Temporada"),

        new Producto("PROD-105", "Cámara de Seguridad Wi-Fi 2K", "Hogar Inteligente", 699.00m, "videocam", true,
            "Resolución 2K con visión nocturna a color, detección de personas, audio bidireccional y grabación en la nube.",
            "Seguridad"),

        // Sin precio previo en el repositorio: valores de demostracion.
        new Producto("SKU-DEMO-001", "Producto de demostración 1", "Demo", 100.00m, "inventory_2", true,
            "Artículo de demostración con existencias en CD-CENTRAL y TIENDA-01-CENTRAL.", "Demo"),

        new Producto("SKU-DEMO-002", "Producto de demostración 2", "Demo", 250.00m, "inventory_2", true,
            "Artículo de demostración con existencias en CD-CENTRAL.", "Demo"),

        new Producto("SKU-DEMO-003", "Producto de demostración 3", "Demo", 75.00m, "inventory_2", true,
            "Artículo de demostración con existencias en TIENDA-01-CENTRAL.", "Demo")
    };
}
