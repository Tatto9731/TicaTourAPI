using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using TicaTourShared.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// FIX: registrar TimeProvider para Identity en .NET 8
builder.Services.AddSingleton(TimeProvider.System);

// 1) Registras el DbContext apuntando a la misma BD
var cs = builder.Configuration.GetConnectionString("DefaultConnection");

// PostgreSQL:
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(cs
    // IMPORTANTE: si centralizaste migraciones en otro proyecto (p.ej. "TicaTour.Data")
    // descomenta y pon el nombre del ensamblado de migraciones:
    , b => b.MigrationsAssembly("TicaTourShared.Data")
    )
);

// 2) Registras Identity usando TU ApplicationUser y ese DbContext
builder.Services
    .AddIdentityCore<User>(opt =>
    {
        opt.Password.RequiredLength = 6;
        opt.Password.RequireDigit = false;
        opt.Password.RequireNonAlphanumeric = false;
        opt.Password.RequireUppercase = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
