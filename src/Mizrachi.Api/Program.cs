using Mizrachi.Infrastructure;

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
