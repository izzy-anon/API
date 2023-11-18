﻿using Microsoft.AspNetCore.Mvc;
using OpenShock.Common.OpenShockDb;
using OpenShock.ServicesCommon.Authentication;

namespace OpenShock.API.Controller.Tokens;

[ApiController]
[Route("/{version:apiVersion}/tokens")]
public sealed partial class TokensController : AuthenticatedSessionControllerBase
{
    private readonly OpenShockContext _db;
    private readonly ILogger<TokensController> _logger;

    public TokensController(OpenShockContext db, ILogger<TokensController> logger)
    {
        _db = db;
        _logger = logger;
    }
}