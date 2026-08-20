using Mizrachi.Infrastructure;
using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            // The only call into Infrastructure. Which store backs the API is decided by
            // configuration inside here, never by a code change (NFR-1.3).
            builder.Services.AddInfrastructure(builder.Configuration);

            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
            }

            var app = builder.Build();

            // A file-backed provider prepares its store before the first request rather than on
            // it (NFR-1.4). The interface is provider-agnostic, so no EF type is named here.
            using (var scope = app.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetService<IDatabaseInitializer>();
                initializer?.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            // Registered and mapped only in Development, so the interactive documentation is
            // absent outside it rather than merely unreachable (NFR-2.7).
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
