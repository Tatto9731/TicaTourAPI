using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TicaTourShared.Data;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IConfiguration _config;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
    }

    // ===== DTOs inline (para que no falte nada) =====
    public sealed class LoginRequest
    {
        public string UserNameOrEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string TokenType { get; set; } = "Bearer";
        public IEnumerable<string> Roles { get; set; } = Enumerable.Empty<string>();
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    /// <summary>Login que devuelve un JWT listo para Swagger.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserNameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Credenciales inválidas.");

        // Buscar por username o email
        var user = await _userManager.FindByNameAsync(request.UserNameOrEmail)
                   ?? await _userManager.FindByEmailAsync(request.UserNameOrEmail);

        if (user is null)
            return Unauthorized("Usuario o contraseña incorrectos.");

        var signIn = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signIn.Succeeded)
            return Unauthorized("Usuario o contraseña incorrectos.");

        var roles = await _userManager.GetRolesAsync(user);

        var (token, exp) = GenerateJwt(user, roles);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresAtUtc = exp,
            Roles = roles,
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty
        });
    }

    /// <summary>Endpoint protegido de prueba.</summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value);
        return Ok(new { User = User.Identity?.Name, Roles = roles });
    }

    // ===== Helper para generar JWT =====
    private (string token, DateTime expiresUtc) GenerateJwt(User user, IEnumerable<string> roles)
    {
        var issuer = _config["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer no configurado");
        var audience = _config["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience no configurado");
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key no configurado");
        var minutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? m : 60;

        var keyBytes = Encoding.UTF8.GetBytes(key);
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty)
        };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

        var expires = DateTime.UtcNow.AddMinutes(minutes);

        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
