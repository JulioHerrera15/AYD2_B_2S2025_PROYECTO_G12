using Microsoft.EntityFrameworkCore;
using SgoLogic.Auditoria.Worker.Domain;
using SgoLogic.BuildingBlocks.Idempotencia;

namespace SgoLogic.Auditoria.Worker.Infrastructure;

/// <summary>
/// DbContext propio de Auditoria, contra su propia base (db-auditoria). Trae
/// mensajes_procesados porque el consumidor deduplica con EventConsumerBase; no
/// trae outbox_messages porque Auditoria no publica eventos derivados, solo
/// registra.
///
/// Esta misma clase se instancia con dos cadenas de conexion distintas segun el
/// momento (ver Program.cs): con el usuario propietario para migrar el esquema y
/// aplicar permisos/trigger, y con el usuario restringido (solo INSERT+SELECT
/// sobre eventos_auditoria) para el uso normal en el consumidor y en la API de
/// consulta.
/// </summary>
public sealed class AuditoriaDbContext : DbContext
{
    public AuditoriaDbContext(DbContextOptions<AuditoriaDbContext> opciones) : base(opciones)
    {
    }

    /// <summary>Solo insercion y consulta: no hay ningun metodo en el servicio que llame a Update ni Remove sobre este DbSet.</summary>
    public DbSet<EventoAuditoria> EventosAuditoria => Set<EventoAuditoria>();

    public DbSet<MensajeProcesado> MensajesProcesados => Set<MensajeProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EventoAuditoriaConfiguration());
        modelBuilder.ApplyConfiguration(new MensajeProcesadoConfiguration());
    }
}